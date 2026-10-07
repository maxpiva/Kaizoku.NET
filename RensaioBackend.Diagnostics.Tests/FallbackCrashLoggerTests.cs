using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using RensaioBackend.Utils;
using Xunit;

namespace RensaioBackend.Diagnostics.Tests;

public sealed class FallbackCrashLoggerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rensaio-crash-tests-" + Guid.NewGuid().ToString("N"));

    public FallbackCrashLoggerTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private async Task<int> Run(string mode, string? path = null)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(CrashLoggerProducer).Assembly.Location);
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(path ?? _directory);
        start.Environment["COMPlus_DbgEnableMiniDump"] = "0";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
        catch (TimeoutException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw;
        }
        await Task.WhenAll(stdout, stderr);
        if (mode != "fatal" && mode != "known-fatal" && mode != "diagnostic-rotation")
            Assert.True(process.ExitCode == 0, $"Producer {mode} exited {process.ExitCode}: {await stdout} {await stderr}");
        return process.ExitCode;
    }

    private string StreamText(string name) => string.Join("\n", Directory.GetFiles(_directory, name + "*").Select(File.ReadAllText));

    private void AssertBounded()
    {
        var files = Directory.GetFiles(_directory);
        Assert.True(files.Length <= 2 * FallbackCrashLogger.RetainedFileCount);
        Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, FallbackCrashLogger.MaxFileBytes));
        Assert.InRange(files.Sum(file => new FileInfo(file).Length), 1,
            2L * FallbackCrashLogger.RetainedFileCount * FallbackCrashLogger.MaxFileBytes);
    }

    [Fact]
    public async Task CaughtExceptionsDoNotLogByDefault()
    {
        Assert.Equal(0, await Run("default"));
        Assert.False(File.Exists(Path.Combine(_directory, "firstchance-.log")));
        Assert.DoesNotContain("repeated-marker", StreamText("crash-.log"));
        Assert.Contains("finished-marker", StreamText("crash-.log"));
        AssertBounded();
    }

    [Fact]
    public async Task OptInProducerSuppressesKnownNoiseRepeatsAndUniqueFlood()
    {
        Assert.Equal(0, await Run("diagnostics"));
        var text = StreamText("firstchance-.log");
        Assert.Equal(FallbackCrashLogger.FirstChanceEntriesPerMinute, Regex.Matches(text, "CRASH: FIRSTCHANCE:").Count);
        Assert.Single(Regex.Matches(text, "Message:  repeated-marker").Cast<Match>());
        Assert.Contains("suppressed 20036 repeated/rate-limited exceptions", text);
        Assert.DoesNotContain("known-settings-noise", text);
        Assert.DoesNotContain("known-date-noise", text);
        Assert.DoesNotContain("FIRSTCHANCE", StreamText("crash-.log"));
        AssertBounded();
    }

    [Fact]
    public async Task LargeUnicodeEntriesRotateAndRetainLatestFatal()
    {
        Assert.Equal(0, await Run("rotation"));
        Assert.Equal(FallbackCrashLogger.RetainedFileCount, Directory.GetFiles(_directory, "crash-.log*").Length);
        Assert.Contains("last-fatal-marker", File.ReadAllText(Path.Combine(_directory, "crash-.log")));
        Assert.Contains("truncated", StreamText("crash-.log"));
        // Strict decoding detects a split UTF-8 codepoint at the entry boundary.
        foreach (var file in Directory.GetFiles(_directory))
        {
            var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(file));
            var entries = Regex.Matches(text, @"(?s)\[.*?--- end crash entry ---\r?\n").Cast<Match>();
            Assert.NotEmpty(entries);
            Assert.All(entries, entry => Assert.InRange(Encoding.UTF8.GetByteCount(entry.Value),
                1, FallbackCrashLogger.MaxEntryBytes));
        }
        AssertBounded();
    }

    [Theory]
    [InlineData("threads")]
    [InlineData("processes")]
    public async Task ConcurrentWritersKeepCompleteRecordsAndBoundedArchives(string mode)
    {
        if (mode == "processes")
            Assert.All(await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Run(mode))), code => Assert.Equal(0, code));
        else
            Assert.Equal(0, await Run(mode));
        Assert.Equal(FallbackCrashLogger.RetainedFileCount,
            Directory.GetFiles(_directory, "crash-.log*").Length);
        var records = StreamText("crash-.log").Split('\n').Where(line => line.Contains("BEGIN-")).ToArray();
        // Two completed archives must be full of complete records, not just a
        // handful of successful writes hiding contention-related data loss.
        Assert.True(records.Length >= 2 * (FallbackCrashLogger.MaxFileBytes / (8192 + 256)));
        Assert.All(records, line => Assert.Matches(@"BEGIN-(\d+)-x{8192}-END-\1\r?$", line));
        AssertBounded();
    }

    [Theory]
    [InlineData("fatal", "fatal-marker")]
    [InlineData("known-fatal", "fatal-settings-marker")]
    [InlineData("diagnostic-rotation", "fatal-marker")]
    public async Task RealUnhandledExceptionsBypassDiagnosticFiltersAndQuota(string mode, string marker)
    {
        Assert.NotEqual(0, await Run(mode));
        var text = File.ReadAllText(Path.Combine(_directory, "crash-.log"));
        Assert.Contains("APPDOMAIN UNHANDLED EXCEPTION (terminating=True)", text);
        Assert.Contains(marker, text);
        Assert.Contains("StackTrace:", text);
        Assert.Contains("CrashLoggerProducer.Main", text);
        if (mode == "fatal") Assert.Contains("inner-fatal-marker", text);
        if (mode == "diagnostic-rotation")
        {
            Assert.Equal(FallbackCrashLogger.RetainedFileCount,
                Directory.GetFiles(_directory, "firstchance-.log*").Length);
            Assert.DoesNotContain("FIRSTCHANCE", text);
        }
        AssertBounded();
    }

    [Fact]
    public async Task OversizedLegacyFilesAreBoundedBeforeFirstAppendAndKeepTheirTail()
    {
        foreach (var stream in new[] { "crash-.log", "firstchance-.log" })
        for (var i = 0; i < FallbackCrashLogger.RetainedFileCount; i++)
        {
            var file = Path.Combine(_directory, stream + (i == 0 ? "" : "." + i));
            File.WriteAllText(file, new string('界', FallbackCrashLogger.MaxFileBytes) + "legacy-tail-marker\n");
        }
        Assert.Equal(0, await Run("legacy"));
        Assert.Contains("legacy-tail-marker", StreamText("crash-.log"));
        Assert.Contains("after-initialize-marker", StreamText("crash-.log"));
        foreach (var file in Directory.GetFiles(_directory))
            _ = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(file));
        AssertBounded();
    }

    [Fact]
    public async Task UnavailableDirectoryDoesNotEscapeOrRecursivelyLog()
    {
        var file = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(file, "preserve");
        Assert.Equal(0, await Run("unwritable", file));
        Assert.Equal("preserve", File.ReadAllText(file));
    }
}

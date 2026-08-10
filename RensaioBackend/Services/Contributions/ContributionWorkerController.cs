using Mihon.ExtensionsBridge.Models;
using RensaioBackend.Services.Search;
using RensaioBackend.Services.Search.Discovery;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RensaioBackend.Services.Contributions;

public enum ContributionWorkerOutcomeKind
{
    Completed,
    Yielded
}

public sealed record ContributionWorkerOutcome(ContributionWorkerOutcomeKind Kind, ContributionBatchV1? Batch = null);

public interface IContributionWorkerController
{
    Task<ContributionWorkerOutcome> RunAsync(
        DiscoveryWorkerExtension extension, IReadOnlyCollection<long> sourceIds, Preferences preferences,
        CancellationToken token = default);
}

public sealed class ContributionWorkerController : IContributionWorkerController
{
    private readonly InteractiveDiscoveryGate _interactive;
    private readonly ILogger<ContributionWorkerController> _logger;

    public ContributionWorkerController(InteractiveDiscoveryGate interactive, ILogger<ContributionWorkerController> logger)
    {
        _interactive = interactive;
        _logger = logger;
    }

    public async Task<ContributionWorkerOutcome> RunAsync(
        DiscoveryWorkerExtension extension, IReadOnlyCollection<long> sourceIds, Preferences preferences,
        CancellationToken token = default)
    {
        (string FileName, string? DllPath)? launch = DiscoveryWorkerPool.ResolveWorkerLaunch();
        if (launch == null)
            throw new InvalidOperationException("No contribution worker executable was found; in-process fallback is forbidden.");

        string scratch = Path.Combine(Path.GetTempPath(), "rensaio-contribution-workers", Guid.NewGuid().ToString("N")[..8]);
        var startInfo = new ProcessStartInfo
        {
            FileName = launch.Value.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.Environment["DOTNET_gcServer"] = "0";
        if (launch.Value.DllPath != null)
            startInfo.ArgumentList.Add(launch.Value.DllPath);
        startInfo.ArgumentList.Add(ContributionWorkerProgram.ModeArg);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start contribution worker process.");
        try
        {
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception ex)
            {
                TryKill(process);
                throw new InvalidOperationException("Could not lower contribution worker process priority.", ex);
            }

            Task stderrPump = PumpStderrAsync(process);
            var request = new ContributionWorkerRequest
            {
                ScratchFolder = scratch,
                Preferences = preferences,
                Extension = extension,
                SourceIds = sourceIds.Distinct().ToList(),
                SourceTimeoutSeconds = SourceTimeout.DefaultTimeout.TotalSeconds
            };
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, ContributionWorkerJson.Options)).ConfigureAwait(false);
            process.StandardInput.Close();

            using var activityCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task activity = _interactive.WaitForActivityAsync(activityCts.Token);
            Task<string?> responseLine = ReadProtocolLineAsync(process, token);
            Task completed = await Task.WhenAny(responseLine, activity).ConfigureAwait(false);
            if (completed == activity && _interactive.IsActive)
            {
                TryKill(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await stderrPump.ConfigureAwait(false);
                return new ContributionWorkerOutcome(ContributionWorkerOutcomeKind.Yielded);
            }
            activityCts.Cancel();

            string? line = await responseLine.ConfigureAwait(false);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            await stderrPump.ConfigureAwait(false);
            if (line == null)
                throw new InvalidOperationException($"Contribution worker exited with code {process.ExitCode} without a response.");
            ContributionWorkerResponse response = JsonSerializer.Deserialize<ContributionWorkerResponse>(line, ContributionWorkerJson.Options)
                ?? throw new InvalidOperationException("Contribution worker returned an invalid response.");
            if (!response.Success)
                throw new InvalidOperationException(response.Error ?? "Contribution worker failed.");
            return new ContributionWorkerOutcome(ContributionWorkerOutcomeKind.Completed, response.Batch);
        }
        finally
        {
            TryKill(process);
            try { Directory.Delete(scratch, true); } catch { }
        }
    }

    private async Task<string?> ReadProtocolLineAsync(Process process, CancellationToken token)
    {
        while (true)
        {
            string? line = await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
            if (line == null)
                return null;
            if (line.StartsWith(ContributionWorkerJson.LinePrefix, StringComparison.Ordinal))
                return line[ContributionWorkerJson.LinePrefix.Length..];
            _logger.LogDebug("[contribution worker {Pid} stray stdout] {Line}", process.Id, line);
        }
    }

    private async Task PumpStderrAsync(Process process)
    {
        try
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                _logger.LogDebug("[contribution worker {Pid}] {Line}", process.Id, line);
        }
        catch { }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch { }
    }
}

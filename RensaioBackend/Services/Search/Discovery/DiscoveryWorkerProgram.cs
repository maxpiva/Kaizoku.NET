using Microsoft.Extensions.Logging;
using Mihon.ExtensionsBridge.Core.Runtime;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Text.Json;

namespace RensaioBackend.Services.Search.Discovery;

/// <summary>
/// Entry point for the short-lived discovery-search worker process
/// (<c>RensaioBackend --discovery-worker</c>). Reads one <see cref="DiscoveryWorkerInput"/> from
/// stdin, classloads each extension JAR (already downloaded+converted by the parent), runs the
/// search per source and streams <see cref="DiscoveryWorkerEvent"/> JSON lines on stdout.
/// All logging goes to stderr so stdout stays a pure event stream. The process exits when the
/// batch is finished — that exit is what returns the IKVM/classloader memory to the OS, and a
/// misbehaving extension can only take this worker down, never the main backend.
/// </summary>
public static class DiscoveryWorkerProgram
{
    public const string ModeArg = "--discovery-worker";

    private static readonly JsonElement NullMemo = JsonDocument.Parse("null").RootElement.Clone();

    public static bool IsWorkerInvocation(string[] args) => args.Contains(ModeArg);

    public static async Task<int> RunAsync()
    {
        // Claim the REAL stdout first and keep it as the exclusive protocol channel, then point
        // .NET Console.Out at stderr so any stray managed print becomes a harmless log line
        // instead of corrupting the event stream. (Java System.out is redirected to stderr too,
        // inside DiscoveryWorkerRuntime.Initialize, before any extension code loads.)
        var protocolWriter = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(Console.Error);

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace));
        ILogger logger = loggerFactory.CreateLogger("DiscoveryWorker");

        DiscoveryWorkerInput? input;
        try
        {
            using Stream stdin = Console.OpenStandardInput();
            input = await JsonSerializer.DeserializeAsync<DiscoveryWorkerInput>(stdin, DiscoveryWorkerJson.Options).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Discovery worker could not parse its stdin input.");
            return 2;
        }
        if (input == null || string.IsNullOrWhiteSpace(input.ScratchFolder) || input.Extensions.Count == 0)
        {
            logger.LogCritical("Discovery worker received an empty or invalid input document.");
            return 2;
        }

        await using var stdout = protocolWriter;
        object writeLock = new();
        void Emit(DiscoveryWorkerEvent evt)
        {
            // Single prefixed string per event, written under a lock in one WriteLine call, so
            // protocol lines can never interleave with each other. The prefix lets the parent
            // reject any line that did not come from this writer.
            string line = DiscoveryWorkerJson.LinePrefix + JsonSerializer.Serialize(evt, DiscoveryWorkerJson.Options);
            lock (writeLock)
            {
                stdout.WriteLine(line);
            }
        }

        string androidFolder = Path.Combine(input.ScratchFolder, "android");
        string tempFolder = Path.Combine(input.ScratchFolder, "temp");
        try
        {
            DiscoveryWorkerRuntime.Initialize(androidFolder, tempFolder, loggerFactory.CreateLogger("Android"));
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Discovery worker failed to initialize the android compatibility layer.");
            return 3;
        }
        DiscoveryWorkerRuntime.ApplyPreferences(input.Preferences ?? new Mihon.ExtensionsBridge.Models.Preferences(), logger);

        var languageSet = new HashSet<string>(input.Languages, StringComparer.InvariantCultureIgnoreCase);
        TimeSpan searchTimeout = input.SearchTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(input.SearchTimeoutSeconds)
            : SourceTimeout.DefaultTimeout;
        int parallel = Math.Max(1, input.MaxParallelExtensions);
        logger.LogInformation("Discovery worker starting: {Count} extensions, parallelism {Parallel}, query '{Query}'.",
            input.Extensions.Count, parallel, input.Query);

        await Parallel.ForEachAsync(
            input.Extensions,
            new ParallelOptions { MaxDegreeOfParallelism = parallel },
            async (candidate, ct) =>
            {
                string package = candidate.Entry.Extension.Package;
                Emit(new DiscoveryWorkerEvent { Type = DiscoveryWorkerEventTypes.Begin, Package = package });
                IExtensionInterop interop;
                try
                {
                    interop = DiscoveryWorkerRuntime.LoadExtension(candidate.Entry, candidate.Folder,
                        input.ScratchFolder, tempFolder, logger);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to classload discovery extension {Package}.", package);
                    Emit(new DiscoveryWorkerEvent { Type = DiscoveryWorkerEventTypes.ExtensionFailed, Package = package, Error = ex.Message });
                    return;
                }
                try
                {
                    foreach (ISourceInterop src in interop.Sources)
                    {
                        if (!MatchesLanguage(src.Language, languageSet))
                            continue;
                        try
                        {
                            var result = await SourceTimeout
                                .RunAsync(c => src.SearchAsync(1, input.Query, c), searchTimeout, ct)
                                .ConfigureAwait(false);
                            // Mangas itself can be null when a source doesn't implement search
                            // (same guard as the parent's in-process path).
                            if (result?.Mangas == null || result.Mangas.Count == 0)
                                continue;
                            Emit(new DiscoveryWorkerEvent
                            {
                                Type = DiscoveryWorkerEventTypes.SourceResult,
                                Package = package,
                                SourceId = src.Id,
                                SourceName = src.Name,
                                SourceLanguage = src.Language,
                                Mangas = Sanitize(result.Mangas)
                            });
                        }
                        catch (TimeoutException)
                        {
                            logger.LogWarning("Discovery search for source {Name} timed out after {Seconds}s; skipping.",
                                src.Name, searchTimeout.TotalSeconds);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Error in discovery search for source {Name}: {Message}", src.Name, ex.Message);
                        }
                    }
                    Emit(new DiscoveryWorkerEvent { Type = DiscoveryWorkerEventTypes.ExtensionDone, Package = package });
                }
                finally
                {
                    try { (interop as IDisposable)?.Dispose(); } catch { }
                }
            }).ConfigureAwait(false);

        Emit(new DiscoveryWorkerEvent { Type = DiscoveryWorkerEventTypes.Done });
        logger.LogInformation("Discovery worker finished; shutting down.");
        DiscoveryWorkerRuntime.Shutdown(logger);
        return 0;
    }

    private static bool MatchesLanguage(string? language, HashSet<string> languageSet)
        => !string.IsNullOrEmpty(language) && (language == "all" || languageSet.Contains(language));

    /// <summary>
    /// An unset <see cref="Manga.Memo"/> is an Undefined JsonElement, which System.Text.Json
    /// refuses to serialize — replace it with an explicit null before the mangas cross the pipe.
    /// Null list entries (a misbehaving extension can hand those back) are dropped.
    /// </summary>
    private static List<ParsedManga> Sanitize(List<ParsedManga> mangas)
    {
        var sane = new List<ParsedManga>(mangas.Count);
        foreach (ParsedManga manga in mangas)
        {
            if (manga == null)
                continue;
            if (manga.Memo.ValueKind == JsonValueKind.Undefined)
                manga.Memo = NullMemo;
            sane.Add(manga);
        }
        return sane;
    }
}

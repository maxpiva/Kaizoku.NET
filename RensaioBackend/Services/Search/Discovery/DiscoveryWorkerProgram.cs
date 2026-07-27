using Microsoft.Extensions.Logging;
using Mihon.ExtensionsBridge.Core.Runtime;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using System.Text.Json;

namespace RensaioBackend.Services.Search.Discovery;

/// <summary>
/// Entry point for the discovery-search worker process (<c>RensaioBackend --discovery-worker</c>).
/// Reads one <see cref="DiscoveryWorkerInit"/> line from stdin, then serves
/// <see cref="DiscoveryWorkerRequest"/> lines until stdin closes (warm pool: the process stays
/// resident between requests, keeping classloaded extensions warm because loaded JARs can never
/// be unloaded anyway). Events stream as prefixed JSON lines on stdout; logging goes to stderr.
/// A misbehaving extension can only take this worker down, never the main backend; the parent
/// recycles workers on idle timeout, size growth or memory use to reclaim their memory.
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

        using var stdin = new StreamReader(Console.OpenStandardInput());
        DiscoveryWorkerInit? init;
        try
        {
            string? initLine = await stdin.ReadLineAsync().ConfigureAwait(false);
            init = initLine == null ? null : JsonSerializer.Deserialize<DiscoveryWorkerInit>(initLine, DiscoveryWorkerJson.Options);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Discovery worker could not parse its init line.");
            return 2;
        }
        if (init == null || string.IsNullOrWhiteSpace(init.ScratchFolder))
        {
            logger.LogCritical("Discovery worker received an empty or invalid init document.");
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

        string androidFolder = Path.Combine(init.ScratchFolder, "android");
        string tempFolder = Path.Combine(init.ScratchFolder, "temp");
        try
        {
            DiscoveryWorkerRuntime.Initialize(androidFolder, tempFolder, loggerFactory.CreateLogger("Android"));
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Discovery worker failed to initialize the android compatibility layer.");
            return 3;
        }
        DiscoveryWorkerRuntime.ApplyPreferences(init.Preferences ?? new Mihon.ExtensionsBridge.Models.Preferences(), logger);
        logger.LogInformation("Discovery worker ready; awaiting requests.");

        // Warm cache of classloaded extensions: package -> (version, interop). Never disposed
        // between requests — loaded JARs cannot be unloaded, so keeping the interop costs nothing
        // extra and makes repeat sweeps skip the classload entirely.
        var loaded = new Dictionary<string, (string Version, IExtensionInterop Interop)>(StringComparer.OrdinalIgnoreCase);

        string? requestLine;
        while ((requestLine = await stdin.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrWhiteSpace(requestLine))
                continue;
            DiscoveryWorkerRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<DiscoveryWorkerRequest>(requestLine, DiscoveryWorkerJson.Options);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Discovery worker could not parse a request line; answering with an empty done.");
                Emit(new DiscoveryWorkerEvent { Type = DiscoveryWorkerEventTypes.Done, Error = "unparseable request" });
                continue;
            }
            if (request == null || request.Type == DiscoveryWorkerRequestTypes.Exit)
                break;

            try
            {
                if (request.Type == DiscoveryWorkerRequestTypes.Search)
                    await HandleSearchAsync(request, loaded, init.ScratchFolder, tempFolder, Emit, logger).ConfigureAwait(false);
                else if (request.Type == DiscoveryWorkerRequestTypes.Details)
                    await HandleDetailsAsync(request, loaded, init.ScratchFolder, tempFolder, Emit, logger).ConfigureAwait(false);
                else
                    logger.LogWarning("Discovery worker received an unknown request type '{Type}'.", request.Type);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Discovery worker request of type '{Type}' failed.", request.Type);
            }
            Emit(new DiscoveryWorkerEvent { Type = DiscoveryWorkerEventTypes.Done });
        }

        logger.LogInformation("Discovery worker input closed; shutting down ({Count} extensions were loaded).", loaded.Count);
        DiscoveryWorkerRuntime.Shutdown(logger);
        return 0;
    }

    /// <summary>
    /// Returns the interop for an extension, classloading it only when not already warm (or when
    /// the cached version differs from the requested one — the old interop stays leaked, as
    /// classloaders cannot unload; the parent recycles grown workers).
    /// </summary>
    private static IExtensionInterop LoadOrReuse(DiscoveryWorkerExtension candidate,
        Dictionary<string, (string Version, IExtensionInterop Interop)> loaded,
        string scratchFolder, string tempFolder, ILogger logger)
    {
        string package = candidate.Entry.Extension.Package;
        string version = candidate.Entry.Extension.Version ?? string.Empty;
        lock (loaded)
        {
            if (loaded.TryGetValue(package, out var warm) && warm.Version == version)
                return warm.Interop;
        }
        IExtensionInterop interop = DiscoveryWorkerRuntime.LoadExtension(candidate.Entry, candidate.Folder,
            scratchFolder, tempFolder, logger);
        lock (loaded)
        {
            if (loaded.TryGetValue(package, out var stale) && stale.Version != version)
                logger.LogInformation("Extension {Package} reloaded at version {Version} (old {Old} stays resident until recycle).",
                    package, version, stale.Version);
            loaded[package] = (version, interop);
        }
        return interop;
    }

    private static async Task HandleSearchAsync(DiscoveryWorkerRequest request,
        Dictionary<string, (string Version, IExtensionInterop Interop)> loaded,
        string scratchFolder, string tempFolder,
        Action<DiscoveryWorkerEvent> emit, ILogger logger)
    {
        var languageSet = new HashSet<string>(request.Languages, StringComparer.InvariantCultureIgnoreCase);
        TimeSpan searchTimeout = request.SearchTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(request.SearchTimeoutSeconds)
            : SourceTimeout.DefaultTimeout;
        int parallel = Math.Max(1, request.MaxParallelExtensions);
        logger.LogInformation("Discovery worker searching: {Count} extensions, parallelism {Parallel}, query '{Query}'.",
            request.Extensions.Count, parallel, request.Query);

        await Parallel.ForEachAsync(
            request.Extensions,
            new ParallelOptions { MaxDegreeOfParallelism = parallel },
            async (candidate, ct) =>
            {
                string package = candidate.Entry.Extension.Package;
                emit(new DiscoveryWorkerEvent { Type = DiscoveryWorkerEventTypes.Begin, Package = package });
                IExtensionInterop interop;
                try
                {
                    interop = LoadOrReuse(candidate, loaded, scratchFolder, tempFolder, logger);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to classload discovery extension {Package}.", package);
                    emit(new DiscoveryWorkerEvent { Type = DiscoveryWorkerEventTypes.ExtensionFailed, Package = package, Error = ex.Message });
                    return;
                }
                foreach (ISourceInterop src in interop.Sources)
                {
                    if (!MatchesLanguage(src.Language, languageSet))
                        continue;
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var result = await SourceTimeout
                            .RunAsync(c => src.SearchAsync(1, request.Query, c), searchTimeout, ct)
                            .ConfigureAwait(false);
                        // Per-source timing instrumentation: lets the parent (debug log) show how
                        // batch wall time is composed and whether searches overlap.
                        logger.LogInformation("Source {Name} searched in {Ms}ms ({Count} results).",
                            src.Name, stopwatch.ElapsedMilliseconds, result?.Mangas?.Count ?? 0);
                        // Mangas itself can be null when a source doesn't implement search
                        // (same guard as the parent's in-process path).
                        if (result?.Mangas == null || result.Mangas.Count == 0)
                            continue;
                        emit(new DiscoveryWorkerEvent
                        {
                            Type = DiscoveryWorkerEventTypes.SourceResult,
                            Package = package,
                            SourceId = src.Id,
                            SourceName = src.Name,
                            SourceLanguage = src.Language,
                            Headers = SafeImageHeaders(src, logger),
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
                        logger.LogWarning(ex, "Error in discovery search for source {Name}: {Message} ({Ms}ms)",
                            src.Name, ex.Message, stopwatch.ElapsedMilliseconds);
                    }
                }
                emit(new DiscoveryWorkerEvent { Type = DiscoveryWorkerEventTypes.ExtensionDone, Package = package });
            }).ConfigureAwait(false);
    }

    private static async Task HandleDetailsAsync(DiscoveryWorkerRequest request,
        Dictionary<string, (string Version, IExtensionInterop Interop)> loaded,
        string scratchFolder, string tempFolder,
        Action<DiscoveryWorkerEvent> emit, ILogger logger)
    {
        DiscoveryWorkerExtension? candidate = request.Extensions.FirstOrDefault();
        string? package = candidate?.Entry?.Extension?.Package;
        if (candidate == null || package == null || request.SourceId == null || string.IsNullOrEmpty(request.MangaJson))
        {
            emit(new DiscoveryWorkerEvent { Type = DiscoveryWorkerEventTypes.Details, Package = package, SourceId = request.SourceId, Error = "invalid details request" });
            return;
        }
        try
        {
            IExtensionInterop interop = LoadOrReuse(candidate, loaded, scratchFolder, tempFolder, logger);
            ISourceInterop? src = interop.Sources.FirstOrDefault(s => s.Id == request.SourceId.Value);
            if (src == null)
                throw new InvalidOperationException($"Source {request.SourceId} not found in extension {package}.");
            Manga manga = JsonSerializer.Deserialize<Manga>(request.MangaJson)
                ?? throw new InvalidOperationException("Could not deserialize the manga.");
            TimeSpan timeout = request.SearchTimeoutSeconds > 0
                ? TimeSpan.FromSeconds(request.SearchTimeoutSeconds)
                : SourceTimeout.DefaultTimeout;
            var update = await SourceTimeout
                .RunAsync(c => src.GetDetailsAndChaptersAsync(manga, c), timeout, CancellationToken.None)
                .ConfigureAwait(false);
            emit(new DiscoveryWorkerEvent
            {
                Type = DiscoveryWorkerEventTypes.Details,
                Package = package,
                SourceId = request.SourceId,
                ChapterCount = update?.Chapters?.Count,
                MangaStatus = update?.Manga != null ? (int)update.Manga.Status : null
            });
            // Accounting event so the parent's per-request log reads "1 done" for a served
            // details request instead of the ambiguous "0 done, 0 failed".
            emit(new DiscoveryWorkerEvent { Type = DiscoveryWorkerEventTypes.ExtensionDone, Package = package });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Discovery details request failed for {Package}|{SourceId}.", package, request.SourceId);
            emit(new DiscoveryWorkerEvent { Type = DiscoveryWorkerEventTypes.Details, Package = package, SourceId = request.SourceId, Error = ex.Message });
            emit(new DiscoveryWorkerEvent { Type = DiscoveryWorkerEventTypes.ExtensionFailed, Package = package, Error = ex.Message });
        }
    }

    private static Dictionary<string, string>? SafeImageHeaders(ISourceInterop src, ILogger logger)
    {
        try
        {
            var headers = src.GetImageRequestHeaders();
            return headers.Count > 0 ? headers : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not collect image headers for source {Name}.", src.Name);
            return null;
        }
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

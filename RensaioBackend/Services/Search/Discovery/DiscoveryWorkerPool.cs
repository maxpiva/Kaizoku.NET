using Mihon.ExtensionsBridge.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RensaioBackend.Services.Search.Discovery;

/// <summary>
/// Per-request outcome of one worker interaction, from the parent's point of view.
/// </summary>
public class DiscoveryWorkerBatchOutcome
{
    /// <summary>Extensions that emitted extensionDone.</summary>
    public HashSet<string> Completed { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Extensions that failed in a managed way inside the worker (bad jar etc.) — not retryable.</summary>
    public HashSet<string> FailedManaged { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Extensions that emitted begin but never finished when the worker died — crash suspects.</summary>
    public HashSet<string> Suspects { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>True when the worker emitted the request's final done event.</summary>
    public bool CleanExit { get; set; }
}

/// <summary>Per-sweep launch/runtime parameters handed to the pool by the search service.</summary>
public class DiscoveryWorkerContext
{
    public required (string FileName, string? DllPath) Launch { get; init; }
    public required Preferences Preferences { get; init; }
    public required TimeSpan InactivityTimeout { get; init; }
    public required int BatchSize { get; init; }
    public required bool WarmPoolEnabled { get; init; }
    public required TimeSpan IdleTimeout { get; init; }
    /// <summary>Hard ceiling on live worker processes, not just concurrent ones.</summary>
    public required int MaxWorkers { get; init; }
}

/// <summary>
/// Warm pool of discovery worker processes (<c>RensaioBackend --discovery-worker</c>).
///
/// Workers stay resident between sweeps with their classloaded extensions warm (loaded JARs can
/// never be unloaded, so re-spawning per sweep only re-pays classload time — keeping the process
/// costs the same memory for a while but makes repeat sweeps pure search-network time). The pool
/// plans batches so a warm worker gets exactly the extensions it already has loaded and only new
/// extensions spawn fresh workers. Workers are recycled when idle past the configured timeout,
/// when their loaded set grows past <see cref="GrowthCapFactor"/> × batch size, when their working
/// set exceeds <see cref="MemoryLimitBytes"/>, or when the warm pool is disabled. A cancelled or
/// crashed worker is simply removed — the next sweep respawns what it needs.
/// </summary>
public class DiscoveryWorkerPool : IDisposable
{
    private const int GrowthCapFactor = 4;
    private const long MemoryLimitBytes = 1536L * 1024 * 1024;

    private sealed class WorkerHandle
    {
        public required Process Process { get; init; }
        public required string ScratchFolder { get; init; }
        public int Pid => Process.Id;
        /// <summary>package -> extension version currently classloaded in this worker.</summary>
        public Dictionary<string, string> Loaded { get; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
        public bool Busy { get; set; }
        public bool Doomed { get; set; }
        public Queue<string> StderrTail { get; } = new();
        public Task? StderrPump { get; set; }
    }

    private readonly object _lock = new();
    private readonly List<WorkerHandle> _workers = new();
    private readonly ILogger<DiscoveryWorkerPool> _logger;
    private readonly Timer _reaper;
    private volatile bool _warmEnabled = true;
    private TimeSpan _idleTimeout = TimeSpan.FromMinutes(10);
    private int _batchSize = 10;
    private int _maxWorkers = 2;
    /// <summary>Signalled whenever a worker is released or removed, so acquirers waiting on the
    /// process ceiling can re-check instead of spawning past it.</summary>
    private readonly SemaphoreSlim _slotFreed = new(0);

    public DiscoveryWorkerPool(ILogger<DiscoveryWorkerPool> logger)
    {
        _logger = logger;
        _reaper = new Timer(_ => ReapIdleWorkers(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Locates how to launch a worker. Prefers our own executable (normal backend), then the
    /// published apphost next to us (RensaioTray hosting the backend in-process), then the dotnet
    /// muxer with RensaioBackend.dll. Null means workers are unavailable and the caller should
    /// fall back to the in-process search path.
    /// </summary>
    public static (string FileName, string? DllPath)? ResolveWorkerLaunch()
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) &&
            Path.GetFileNameWithoutExtension(processPath).Equals("RensaioBackend", StringComparison.OrdinalIgnoreCase))
            return (processPath, null);

        string baseDir = AppContext.BaseDirectory;
        string exe = Path.Combine(baseDir, OperatingSystem.IsWindows() ? "RensaioBackend.exe" : "RensaioBackend");
        if (File.Exists(exe))
            return (exe, null);

        string dll = Path.Combine(baseDir, "RensaioBackend.dll");
        if (File.Exists(dll))
            return ("dotnet", dll);
        return null;
    }

    /// <summary>Applies the sweep's pool-related settings (warm toggle, idle timeout, batch size).</summary>
    public void Configure(DiscoveryWorkerContext context)
    {
        _warmEnabled = context.WarmPoolEnabled;
        _idleTimeout = context.IdleTimeout > TimeSpan.Zero ? context.IdleTimeout : TimeSpan.FromMinutes(10);
        _batchSize = Math.Max(1, context.BatchSize);
        _maxWorkers = Math.Max(1, context.MaxWorkers);
        if (!_warmEnabled)
            ReapAll("warm pool disabled");
    }

    /// <summary>
    /// Splits the prepared extensions into batches aligned with the warm workers' loaded sets:
    /// each warm worker gets one batch of exactly the extensions it already has loaded (version
    /// matched), the remainder is chunked into fresh batches of <paramref name="batchSize"/>.
    /// </summary>
    public List<List<DiscoveryWorkerExtension>> PlanBatches(IReadOnlyCollection<DiscoveryWorkerExtension> prepared, int batchSize)
    {
        var batches = new List<List<DiscoveryWorkerExtension>>();
        var remaining = prepared.ToDictionary(e => e.Entry.Extension.Package, e => e, StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            foreach (WorkerHandle worker in _workers.Where(w => !w.Doomed))
            {
                var mine = new List<DiscoveryWorkerExtension>();
                foreach (var kv in worker.Loaded)
                {
                    if (remaining.TryGetValue(kv.Key, out DiscoveryWorkerExtension? candidate) &&
                        (candidate.Entry.Extension.Version ?? string.Empty) == kv.Value)
                    {
                        mine.Add(candidate);
                        remaining.Remove(kv.Key);
                    }
                }
                if (mine.Count > 0)
                    batches.Add(mine);
            }
        }
        batches.AddRange(remaining.Values.Chunk(batchSize).Select(c => c.ToList()));
        return batches;
    }

    /// <summary>How many extensions of the given set are already warm in resident workers.</summary>
    public int CountWarm(IEnumerable<DiscoveryWorkerExtension> extensions)
    {
        lock (_lock)
        {
            var loaded = _workers.Where(w => !w.Doomed)
                .SelectMany(w => w.Loaded)
                .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
            return extensions.Count(e => loaded.TryGetValue(e.Entry.Extension.Package, out string? v) &&
                                         (e.Entry.Extension.Version ?? string.Empty) == v);
        }
    }

    /// <summary>Runs one search batch on a (preferably warm) worker, streaming its events.</summary>
    public async Task<DiscoveryWorkerBatchOutcome> RunSearchBatchAsync(
        List<DiscoveryWorkerExtension> batch,
        string query, IReadOnlyCollection<string> languages, double searchTimeoutSeconds, int parallelism,
        DiscoveryWorkerContext context,
        Func<DiscoveryWorkerEvent, Task> onEvent,
        CancellationToken token)
    {
        var request = new DiscoveryWorkerRequest
        {
            Type = DiscoveryWorkerRequestTypes.Search,
            Query = query,
            Languages = languages.ToList(),
            SearchTimeoutSeconds = searchTimeoutSeconds,
            MaxParallelExtensions = parallelism,
            Extensions = batch
        };
        WorkerHandle handle = await AcquireWorkerAsync(batch, context, token).ConfigureAwait(false);
        var outcome = new DiscoveryWorkerBatchOutcome();
        try
        {
            outcome = await RunRequestAsync(handle, request, context.InactivityTimeout, onEvent, token).ConfigureAwait(false);
            if (outcome.CleanExit)
            {
                lock (_lock)
                {
                    foreach (DiscoveryWorkerExtension ext in batch)
                    {
                        if (!outcome.FailedManaged.Contains(ext.Entry.Extension.Package))
                            handle.Loaded[ext.Entry.Extension.Package] = ext.Entry.Extension.Version ?? string.Empty;
                    }
                }
            }
            return outcome;
        }
        finally
        {
            ReleaseWorker(handle, outcome.CleanExit);
        }
    }

    /// <summary>
    /// Fetches details (chapter count + status) for one manga through a worker, preferring one
    /// that already has the extension warm. Returns null on any failure.
    /// </summary>
    public async Task<DiscoveryWorkerEvent?> RunDetailsAsync(
        DiscoveryWorkerExtension extension, long sourceId, string mangaJson, double timeoutSeconds,
        DiscoveryWorkerContext context, CancellationToken token)
    {
        var request = new DiscoveryWorkerRequest
        {
            Type = DiscoveryWorkerRequestTypes.Details,
            SearchTimeoutSeconds = timeoutSeconds,
            Extensions = [extension],
            SourceId = sourceId,
            MangaJson = mangaJson
        };
        WorkerHandle handle = await AcquireWorkerAsync(request.Extensions, context, token).ConfigureAwait(false);
        DiscoveryWorkerEvent? answer = null;
        var outcome = new DiscoveryWorkerBatchOutcome();
        string identity = extension.Entry.Extension.Package + "|" + sourceId;
        try
        {
            outcome = await RunRequestAsync(handle, request, context.InactivityTimeout, evt =>
            {
                if (evt.Type == DiscoveryWorkerEventTypes.Details)
                    answer = evt;
                return Task.CompletedTask;
            }, token).ConfigureAwait(false);
            if (answer == null)
            {
                _logger.LogWarning("Discovery details request for {Identity} produced no answer (worker {Pid}, clean exit: {Clean}).",
                    identity, handle.Pid, outcome.CleanExit);
                return null;
            }
            if (answer.Error != null)
            {
                _logger.LogWarning("Discovery details request for {Identity} failed in worker {Pid}: {Error}",
                    identity, handle.Pid, answer.Error);
                return null;
            }
            _logger.LogInformation("Discovery details for {Identity}: {Chapters} chapters, status {Status} (worker {Pid}).",
                identity, answer.ChapterCount, answer.MangaStatus, handle.Pid);
            if (outcome.CleanExit)
            {
                lock (_lock)
                {
                    handle.Loaded[extension.Entry.Extension.Package] = extension.Entry.Extension.Version ?? string.Empty;
                }
            }
            return answer;
        }
        finally
        {
            ReleaseWorker(handle, outcome.CleanExit);
        }
    }

    // ----------------------------------------------------------------- internals

    /// <summary>
    /// Gets a worker for this batch, never letting the pool hold more than
    /// <see cref="DiscoveryWorkerContext.MaxWorkers"/> live processes. Reuse is preferred; at the
    /// ceiling an idle worker that cannot take this batch (grown past the recycle cap) is retired to
    /// make room, and when every worker is busy the caller waits for one to be released rather than
    /// spawning past the ceiling.
    /// </summary>
    private async Task<WorkerHandle> AcquireWorkerAsync(List<DiscoveryWorkerExtension> batch,
        DiscoveryWorkerContext context, CancellationToken token)
    {
        // The details path does not go through Configure(), so keep the ceiling current here too.
        _maxWorkers = Math.Max(1, context.MaxWorkers);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            WorkerHandle? acquired = TryAcquireWorker(batch, context, out WorkerHandle? retire);
            if (retire != null)
                KillWorker(retire, "retired to stay within max workers");
            if (acquired != null)
                return acquired;
            // Either we just made room, or every worker is busy — wait for a release and re-check.
            await _slotFreed.WaitAsync(TimeSpan.FromMilliseconds(250), token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// One attempt at the acquire loop. Returns null when the pool is at its ceiling with every
    /// worker busy; <paramref name="retire"/> is a worker the caller must kill outside the lock.
    /// </summary>
    private WorkerHandle? TryAcquireWorker(List<DiscoveryWorkerExtension> batch,
        DiscoveryWorkerContext context, out WorkerHandle? retire)
    {
        retire = null;
        lock (_lock)
        {
            // 1) An idle worker that already has every extension of this batch loaded (version match).
            WorkerHandle? best = null;
            int bestCover = -1;
            foreach (WorkerHandle w in _workers)
            {
                if (w.Busy || w.Doomed || w.Process.HasExited)
                    continue;
                int cover = batch.Count(e => w.Loaded.TryGetValue(e.Entry.Extension.Package, out string? v) &&
                                             (e.Entry.Extension.Version ?? string.Empty) == v);
                int newOnes = batch.Count - cover;
                if (w.Loaded.Count + newOnes > _batchSize * GrowthCapFactor)
                    continue; // would grow past the recycle cap; don't feed it more
                if (cover > bestCover)
                {
                    bestCover = cover;
                    best = w;
                }
            }
            if (best != null && bestCover > 0)
            {
                best.Busy = true;
                return best;
            }
            // 2) Any idle worker with capacity (avoids a spawn even without warm overlap).
            if (best != null && _warmEnabled)
            {
                best.Busy = true;
                return best;
            }
            // 3) Spawn a fresh worker, but never past the process ceiling.
            _workers.RemoveAll(w => w.Doomed && !w.Busy);
            if (_workers.Count >= _maxWorkers)
            {
                // At the ceiling. Retire the least recently used idle worker — it is one we could
                // not reuse above (grown past the recycle cap), so it would otherwise sit resident
                // holding its heap until the idle reaper eventually collected it.
                WorkerHandle? victim = _workers
                    .Where(w => !w.Busy)
                    .OrderBy(w => w.LastUsedUtc)
                    .FirstOrDefault();
                if (victim == null)
                    return null; // every worker is busy; caller waits for a release
                _workers.Remove(victim);
                retire = victim;
                return null; // retry after the caller kills it, so we never overlap processes
            }
            WorkerHandle spawned = Spawn(context);
            spawned.Busy = true;
            _workers.Add(spawned);
            return spawned;
        }
    }

    private void ReleaseWorker(WorkerHandle handle, bool clean)
    {
        bool kill;
        lock (_lock)
        {
            handle.LastUsedUtc = DateTime.UtcNow;
            handle.Busy = false;
            long memory = 0;
            try
            {
                if (!handle.Process.HasExited)
                {
                    handle.Process.Refresh();
                    memory = handle.Process.WorkingSet64;
                }
            }
            catch { }
            kill = !clean || !_warmEnabled || handle.Doomed || handle.Process.HasExited
                   || handle.Loaded.Count > _batchSize * GrowthCapFactor
                   || memory > MemoryLimitBytes;
            if (kill)
                _workers.Remove(handle);
            else if (memory > 0)
                _logger.LogDebug("Discovery worker {Pid} stays warm: {Count} extensions loaded, {Memory}MB.",
                    handle.Pid, handle.Loaded.Count, memory / (1024 * 1024));
        }
        if (kill)
            KillWorker(handle, "recycled");
        SignalSlotFreed();
    }

    /// <summary>Wakes one acquirer waiting on the process ceiling.</summary>
    private void SignalSlotFreed()
    {
        try { _slotFreed.Release(); } catch (ObjectDisposedException) { }
    }

    private WorkerHandle Spawn(DiscoveryWorkerContext context)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "rensaio-discovery-workers", Guid.NewGuid().ToString("N")[..8]);
        var psi = new ProcessStartInfo
        {
            FileName = context.Launch.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (context.Launch.DllPath != null)
            psi.ArgumentList.Add(context.Launch.DllPath);
        psi.ArgumentList.Add(DiscoveryWorkerProgram.ModeArg);

        Process process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start discovery worker process.");
        var handle = new WorkerHandle { Process = process, ScratchFolder = scratch };
        handle.StderrPump = Task.Run(async () =>
        {
            try
            {
                string? line;
                bool elevated = false;
                while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    // The worker's console logger writes "warn:/fail:/crit: Category" then indented
                    // message lines. Surface those blocks at Information in the parent log so a
                    // worker-side failure reason is visible without enabling Debug.
                    if (line.StartsWith("warn:", StringComparison.Ordinal) ||
                        line.StartsWith("fail:", StringComparison.Ordinal) ||
                        line.StartsWith("crit:", StringComparison.Ordinal))
                        elevated = true;
                    else if (!line.StartsWith(" ", StringComparison.Ordinal))
                        elevated = false;
                    if (elevated)
                        _logger.LogInformation("[worker {Pid}] {Line}", handle.Pid, line);
                    else
                        _logger.LogDebug("[worker {Pid}] {Line}", handle.Pid, line);
                    lock (handle.StderrTail)
                    {
                        handle.StderrTail.Enqueue(line);
                        if (handle.StderrTail.Count > 40)
                            handle.StderrTail.Dequeue();
                    }
                }
            }
            catch { }
        });
        var init = new DiscoveryWorkerInit { ScratchFolder = scratch, Preferences = context.Preferences };
        process.StandardInput.WriteLine(JsonSerializer.Serialize(init, DiscoveryWorkerJson.Options));
        process.StandardInput.Flush();
        _logger.LogInformation("Discovery worker {Pid} spawned (scratch {Scratch}).", handle.Pid, scratch);
        return handle;
    }

    private async Task<DiscoveryWorkerBatchOutcome> RunRequestAsync(
        WorkerHandle handle, DiscoveryWorkerRequest request, TimeSpan inactivityTimeout,
        Func<DiscoveryWorkerEvent, Task> onEvent, CancellationToken token)
    {
        var outcome = new DiscoveryWorkerBatchOutcome();
        var begun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Process process = handle.Process;
        int pid = handle.Pid;
        bool workerDead = false;

        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, DiscoveryWorkerJson.Options)).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(CancellationToken.None).ConfigureAwait(false);

            while (!outcome.CleanExit)
            {
                string? line;
                try
                {
                    // Inactivity watchdog: every event resets the clock. A worker stuck in native
                    // code emits nothing and gets killed here instead of hanging the search.
                    line = await process.StandardOutput.ReadLineAsync(token).AsTask()
                        .WaitAsync(inactivityTimeout, CancellationToken.None).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Discovery worker {Pid} produced no output for {Seconds}s; killing it.",
                        pid, inactivityTimeout.TotalSeconds);
                    workerDead = true;
                    break;
                }
                if (line == null)
                {
                    workerDead = true; // stdout closed mid-request: the worker died
                    break;
                }
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Only lines bearing the protocol prefix are events; anything else is stray
                // output (extension/native code printing to fd 1 past the stderr redirects) and
                // is expected noise — log quietly, no JSON parse attempt.
                if (!line.StartsWith(DiscoveryWorkerJson.LinePrefix, StringComparison.Ordinal))
                {
                    _logger.LogDebug("[worker {Pid} stray stdout] {Line}", pid, line);
                    continue;
                }

                DiscoveryWorkerEvent? evt;
                try
                {
                    evt = JsonSerializer.Deserialize<DiscoveryWorkerEvent>(
                        line.Substring(DiscoveryWorkerJson.LinePrefix.Length), DiscoveryWorkerJson.Options);
                }
                catch (JsonException ex)
                {
                    // A prefixed line that fails to parse means true mid-line corruption slipped
                    // through — drop it. Extension accounting still works: done/failed events are
                    // tiny separate lines, so the extension is not stranded (and a mangled
                    // done/failed line is covered by the mid-stream crash-suspect path).
                    _logger.LogWarning(ex, "Discovery worker {Pid} emitted a corrupted protocol line; dropping it.", pid);
                    continue;
                }
                if (evt == null)
                    continue;

                switch (evt.Type)
                {
                    case DiscoveryWorkerEventTypes.Begin when evt.Package != null:
                        begun.Add(evt.Package);
                        break;
                    case DiscoveryWorkerEventTypes.ExtensionDone when evt.Package != null:
                        outcome.Completed.Add(evt.Package);
                        await onEvent(evt).ConfigureAwait(false); // per-extension progress for streaming UIs
                        break;
                    case DiscoveryWorkerEventTypes.ExtensionFailed when evt.Package != null:
                        outcome.FailedManaged.Add(evt.Package);
                        _logger.LogWarning("Discovery worker {Pid} could not process extension {Package}: {Error}",
                            pid, evt.Package, evt.Error);
                        await onEvent(evt).ConfigureAwait(false);
                        break;
                    case DiscoveryWorkerEventTypes.Done:
                        outcome.CleanExit = true;
                        break;
                    case DiscoveryWorkerEventTypes.SourceResult:
                    case DiscoveryWorkerEventTypes.Details:
                        await onEvent(evt).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A cancelled mid-request worker is killed (its search threads can't be recalled);
            // the next sweep simply respawns what it needs.
            lock (_lock) { handle.Doomed = true; _workers.Remove(handle); }
            KillWorker(handle, "cancelled mid-request");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "I/O failure talking to discovery worker {Pid}.", pid);
            workerDead = true;
        }

        if (workerDead)
        {
            lock (_lock) { handle.Doomed = true; }
            foreach (string pkg in begun)
            {
                if (!outcome.Completed.Contains(pkg) && !outcome.FailedManaged.Contains(pkg))
                    outcome.Suspects.Add(pkg);
            }
            string tail;
            lock (handle.StderrTail)
            {
                tail = string.Join(Environment.NewLine, handle.StderrTail);
            }
            _logger.LogWarning("Discovery worker {Pid} died mid-request (exit code {Code}); suspects: {Suspects}. Last stderr:{NewLine}{Tail}",
                pid, process.HasExited ? process.ExitCode : -1, string.Join(",", outcome.Suspects), Environment.NewLine, tail);
        }
        else if (outcome.CleanExit)
        {
            _logger.LogInformation("Discovery worker {Pid} answered a {Type} request: {Done} done, {Failed} failed.",
                pid, request.Type, outcome.Completed.Count, outcome.FailedManaged.Count);
        }
        return outcome;
    }

    private void KillWorker(WorkerHandle handle, string reason)
    {
        try
        {
            if (!handle.Process.HasExited)
            {
                // Ask politely first (lets the worker run its android shutdown), then force.
                try
                {
                    handle.Process.StandardInput.Close();
                    if (!handle.Process.WaitForExit(5000))
                        handle.Process.Kill(entireProcessTree: true);
                }
                catch
                {
                    try { handle.Process.Kill(entireProcessTree: true); } catch { }
                }
            }
            // Post-completion nonzero exits are the known IKVM/CEF teardown race — not a crash.
            _logger.LogInformation("Discovery worker {Pid} stopped ({Reason}); exit code {Code}.",
                handle.Pid, reason, SafeExitCode(handle.Process));
            handle.Process.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error stopping discovery worker {Pid}.", handle.Pid);
        }
        try
        {
            if (Directory.Exists(handle.ScratchFolder))
                Directory.Delete(handle.ScratchFolder, recursive: true);
        }
        catch { /* best effort scratch cleanup */ }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.HasExited ? process.ExitCode : 0; } catch { return 0; }
    }

    private void ReapIdleWorkers()
    {
        List<WorkerHandle> victims;
        lock (_lock)
        {
            victims = _workers
                .Where(w => !w.Busy && (w.Doomed || w.Process.HasExited || DateTime.UtcNow - w.LastUsedUtc > _idleTimeout))
                .ToList();
            foreach (WorkerHandle v in victims)
                _workers.Remove(v);
        }
        foreach (WorkerHandle v in victims)
        {
            KillWorker(v, "idle timeout");
            SignalSlotFreed();
        }
    }

    private void ReapAll(string reason)
    {
        List<WorkerHandle> victims;
        lock (_lock)
        {
            victims = _workers.Where(w => !w.Busy).ToList();
            foreach (WorkerHandle v in victims)
                _workers.Remove(v);
        }
        foreach (WorkerHandle v in victims)
        {
            KillWorker(v, reason);
            SignalSlotFreed();
        }
    }

    public void Dispose()
    {
        _reaper.Dispose();
        List<WorkerHandle> victims;
        lock (_lock)
        {
            victims = _workers.ToList();
            _workers.Clear();
        }
        foreach (WorkerHandle v in victims)
            KillWorker(v, "shutdown");
        GC.SuppressFinalize(this);
    }
}

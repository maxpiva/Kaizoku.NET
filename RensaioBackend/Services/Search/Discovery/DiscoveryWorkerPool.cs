using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RensaioBackend.Services.Search.Discovery;

/// <summary>
/// Per-batch outcome of one worker process run, from the parent's point of view.
/// </summary>
public class DiscoveryWorkerBatchOutcome
{
    /// <summary>Extensions that emitted extensionDone.</summary>
    public HashSet<string> Completed { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Extensions that failed in a managed way inside the worker (bad jar etc.) — not retryable.</summary>
    public HashSet<string> FailedManaged { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Extensions that emitted begin but never finished when the worker died — crash suspects.</summary>
    public HashSet<string> Suspects { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>True when the worker emitted its final done event and exited on its own.</summary>
    public bool CleanExit { get; set; }
}

/// <summary>
/// Spawns and supervises one discovery-search worker process (<c>RensaioBackend --discovery-worker</c>):
/// writes the input document to its stdin, streams stdout JSON-line events back to the caller, logs
/// stderr, and kills the worker if it goes silent for longer than the inactivity timeout.
/// </summary>
public static class DiscoveryWorkerPool
{
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

    public static async Task<DiscoveryWorkerBatchOutcome> RunWorkerAsync(
        (string FileName, string? DllPath) launch,
        DiscoveryWorkerInput input,
        TimeSpan inactivityTimeout,
        Func<DiscoveryWorkerEvent, Task> onEvent,
        ILogger logger,
        CancellationToken token)
    {
        var outcome = new DiscoveryWorkerBatchOutcome();
        var begun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var psi = new ProcessStartInfo
        {
            FileName = launch.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (launch.DllPath != null)
            psi.ArgumentList.Add(launch.DllPath);
        psi.ArgumentList.Add(DiscoveryWorkerProgram.ModeArg);

        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start discovery worker process.");
        int pid = process.Id;
        logger.LogInformation("Discovery worker {Pid} started for a batch of {Count} extensions.", pid, input.Extensions.Count);

        // Keep a stderr tail so a crashed worker leaves something actionable in the parent log.
        var stderrTail = new Queue<string>();
        Task stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                logger.LogDebug("[worker {Pid}] {Line}", pid, line);
                lock (stderrTail)
                {
                    stderrTail.Enqueue(line);
                    if (stderrTail.Count > 40)
                        stderrTail.Dequeue();
                }
            }
        }, CancellationToken.None);

        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(input, DiscoveryWorkerJson.Options)).ConfigureAwait(false);
            process.StandardInput.Close();

            while (true)
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
                    logger.LogWarning("Discovery worker {Pid} produced no output for {Seconds}s; killing it.",
                        pid, inactivityTimeout.TotalSeconds);
                    break;
                }
                if (line == null)
                    break;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Only lines bearing the protocol prefix are events; anything else is stray
                // output (extension/native code printing to fd 1 past the stderr redirects) and
                // is expected noise — log quietly, no JSON parse attempt.
                if (!line.StartsWith(DiscoveryWorkerJson.LinePrefix, StringComparison.Ordinal))
                {
                    logger.LogDebug("[worker {Pid} stray stdout] {Line}", pid, line);
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
                    // tiny separate lines, so the extension is not stranded (and if the done/done
                    // event itself were the mangled line, the unclean-exit suspect path covers it).
                    logger.LogWarning(ex, "Discovery worker {Pid} emitted a corrupted protocol line; dropping it.", pid);
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
                        logger.LogWarning("Discovery worker {Pid} could not process extension {Package}: {Error}",
                            pid, evt.Package, evt.Error);
                        await onEvent(evt).ConfigureAwait(false);
                        break;
                    case DiscoveryWorkerEventTypes.Done:
                        outcome.CleanExit = true;
                        break;
                    case DiscoveryWorkerEventTypes.SourceResult:
                        await onEvent(evt).ConfigureAwait(false);
                        break;
                }
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    if (outcome.CleanExit)
                    {
                        // Grace period for the worker's own android shutdown, then force it out.
                        await process.WaitForExitAsync(CancellationToken.None)
                            .WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (TimeoutException) { }
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
            }
            try { await stderrTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); } catch { }
        }

        token.ThrowIfCancellationRequested();

        if (!outcome.CleanExit)
        {
            foreach (string pkg in begun)
            {
                if (!outcome.Completed.Contains(pkg) && !outcome.FailedManaged.Contains(pkg))
                    outcome.Suspects.Add(pkg);
            }
            string tail;
            lock (stderrTail)
            {
                tail = string.Join(Environment.NewLine, stderrTail);
            }
            logger.LogWarning("Discovery worker {Pid} exited uncleanly (exit code {Code}); suspects: {Suspects}. Last stderr:{NewLine}{Tail}",
                pid, process.HasExited ? process.ExitCode : -1, string.Join(",", outcome.Suspects), Environment.NewLine, tail);
        }
        else
        {
            logger.LogInformation("Discovery worker {Pid} finished cleanly: {Done} done, {Failed} failed.",
                pid, outcome.Completed.Count, outcome.FailedManaged.Count);
        }
        return outcome;
    }
}

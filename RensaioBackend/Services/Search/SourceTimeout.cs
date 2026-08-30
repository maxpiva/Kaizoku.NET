using System;
using System.Threading;
using System.Threading.Tasks;

namespace RensaioBackend.Services.Search
{
    /// <summary>
    /// Global concurrency budget for in-flight calls into Mihon source extensions.
    ///
    /// Each source call crosses the IKVM boundary and can spawn/attach worker threads; CEF native
    /// workers and the .NET thread pool compete for the same process thread budget. On desktop
    /// (RensaioTray) builds the AWT/Swing event threads created lazily by JCEF OSR can then fail to
    /// start with "OutOfMemoryError: unable to create native thread" when the budget is exhausted
    /// mid-flight. Serializing source calls through a single global semaphore keeps the number of
    /// concurrently executing extension calls bounded regardless of how many parallel loops
    /// (search, import, latest-series, downloads) are active at once.
    /// </summary>
    public static class SourceTimeoutGate
    {
        /// <summary>
        /// Maximum number of source-extension calls that may be executing concurrently.
        /// Deliberately modest: each call can itself fan out (RxJava observables, FlareSolverr,
        /// WebView/CEF) and the goal is to protect the process thread budget, not to maximize throughput.
        /// </summary>
        private static readonly SemaphoreSlim _sourceCallGate = new(8, 8);

        /// <summary>
        /// Acquires a slot from the global source-call budget. The returned handle must be disposed
        /// (ideally in a finally block) once the source call completes.
        /// </summary>
        public static async Task<IDisposable> AcquireAsync(CancellationToken token)
        {
            await _sourceCallGate.WaitAsync(token).ConfigureAwait(false);
            return new Releaser(_sourceCallGate);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private bool _disposed;

            public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

            public void Dispose()
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                    _disposed = true;
                }
            }
        }
    }

    /// <summary>
    /// Enforces a hard wall-clock timeout on calls into Mihon source extensions
    /// (search, details, chapters, ...).
    ///
    /// Source extensions run third-party Kotlin code (OkHttp + RxJava, sometimes Cloudflare/WebView
    /// or rate-limit interceptors). In headless/Docker environments those can block indefinitely and
    /// fail to honor OkHttp's own callTimeout. That is what makes the library import "freeze" on a
    /// single series for hours: one provider call never returns and nothing above it is bounded.
    ///
    /// <see cref="RunAsync{T}"/> guarantees the awaiting task never waits longer than the timeout,
    /// even if the underlying call subscribes synchronously and blocks its thread, by:
    ///   1. Offloading the call to the thread pool (a synchronous block can't pin the caller),
    ///   2. Racing it against a delay so a non-cancellable call is still abandoned on time, and
    ///   3. Cancelling a linked token so cooperatively-cancellable calls free their resources.
    /// A genuinely stuck call may leak its worker thread until the source's own timeout fires, but the
    /// import keeps moving. The caller's own <see cref="CancellationToken"/> still surfaces as a normal
    /// cancellation; an exceeded budget surfaces as a <see cref="TimeoutException"/>.
    ///
    /// Additionally, every source call is throttled through <see cref="SourceTimeoutGate"/> so the
    /// total number of in-flight extension calls stays within a global budget (see the gate's docs
    /// for why — thread-budget exhaustion on desktop/CEF builds surfaces as a JVM
    /// "OutOfMemoryError: unable to create native thread").
    /// </summary>
    public static class SourceTimeout
    {
        /// <summary>
        /// Default ceiling for a single source operation. Generous enough to cover a slow source or a
        /// Cloudflare solve (FlareSolverrTimeout is 60s) without letting a stuck call run forever.
        /// Matches OkHttp's own callTimeout (2 min) so a leaked worker thread is reaped at roughly the same time.
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

        public static async Task<T> RunAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            TimeSpan timeout,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();

            // Global in-flight budget for source-extension calls (see SourceTimeoutGate).
            using (await SourceTimeoutGate.AcquireAsync(token).ConfigureAwait(false))
            {
                return await RunAsyncInternal(operation, timeout, token).ConfigureAwait(false);
            }
        }

        public static Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token = default)
            => RunAsync(operation, DefaultTimeout, token);

        private static async Task<T> RunAsyncInternal<T>(
            Func<CancellationToken, Task<T>> operation,
            TimeSpan timeout,
            CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(timeout);
            var opToken = timeoutCts.Token;

            // Offload so a source that subscribes synchronously and blocks can't pin the caller.
            var opTask = Task.Run(() => operation(opToken), opToken);

            var finished = await Task.WhenAny(opTask, Task.Delay(timeout)).ConfigureAwait(false);
            if (finished != opTask)
            {
                // Wall-clock timeout: the call is stuck and not honoring cooperative cancellation.
                ObserveLater(opTask);
                token.ThrowIfCancellationRequested(); // a real outer cancellation propagates as cancellation
                throw new TimeoutException($"Source call did not complete within {timeout.TotalSeconds:0}s.");
            }

            try
            {
                return await opTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                // Cooperative cancellation fired because of our timeout, not the caller's token.
                throw new TimeoutException($"Source call did not complete within {timeout.TotalSeconds:0}s.");
            }
        }

        // Make sure an abandoned (timed-out) task's exception is eventually observed so it doesn't
        // surface as an UnobservedTaskException later.
        private static void ObserveLater(Task task)
            => _ = task.ContinueWith(
                static t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }
}

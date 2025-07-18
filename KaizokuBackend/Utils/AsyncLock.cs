namespace KaizokuBackend.Utils
{

    public class AsyncLock
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly Task<IDisposable> _releaser;

        public AsyncLock()
        {
            _releaser = Task.FromResult((IDisposable)new Releaser(this));
        }

        public Task<IDisposable> LockAsync(CancellationToken token = default)
        {
            var wait = _semaphore.WaitAsync(token);
            return wait.IsCompleted ?
                _releaser :
                wait.ContinueWith((_, state) => (IDisposable)state!,
                    _releaser.Result,
                    TaskScheduler.Default);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly AsyncLock _toRelease;
            internal Releaser(AsyncLock toRelease) { _toRelease = toRelease; }
            public void Dispose() { _toRelease._semaphore.Release(); }
        }
    }
}

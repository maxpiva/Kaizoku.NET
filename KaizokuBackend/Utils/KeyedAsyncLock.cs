using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace KaizokuBackend.Utils
{
    public class KeyedAsyncLock
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        public async Task<IDisposable> LockAsync(string key, CancellationToken token = default)
        {
            var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(token).ConfigureAwait(false);
            return new Releaser(this, key, semaphore);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly KeyedAsyncLock _parent;
            private readonly string _key;
            private readonly SemaphoreSlim _semaphore;
            private bool _disposed;

            public Releaser(KeyedAsyncLock parent, string key, SemaphoreSlim semaphore)
            {
                _parent = parent;
                _key = key;
                _semaphore = semaphore;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _semaphore.Release();

                // Try to remove the semaphore if no one is waiting and count is 1 (unlocked)
                if (_semaphore.CurrentCount == 1 && _parent._locks.TryGetValue(_key, out var sem) && sem == _semaphore)
                {
                    _parent._locks.TryRemove(_key, out _);
                    _semaphore.Dispose();
                }

                _disposed = true;
            }
        }
    }
}

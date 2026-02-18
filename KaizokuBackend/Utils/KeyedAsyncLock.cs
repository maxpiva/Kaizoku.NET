using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace KaizokuBackend.Utils
{
    public class KeyedAsyncLock
    {
        private readonly ConcurrentDictionary<string, RefCountedSemaphore> _locks = new();

        public async Task<IDisposable> LockAsync(string key, CancellationToken token = default)
        {
            while (true)
            {
                // Check cancellation at start of each iteration
                token.ThrowIfCancellationRequested();

                var semaphore = _locks.GetOrAdd(key, _ => new RefCountedSemaphore());

                // Increment ref count before waiting - if it was 0, the semaphore is being disposed
                if (semaphore.TryAddRef())
                {
                    try
                    {
                        await semaphore.Semaphore.WaitAsync(token).ConfigureAwait(false);
                        return new Releaser(this, key, semaphore);
                    }
                    catch
                    {
                        // If WaitAsync fails (e.g., cancellation), release the ref we added
                        ReleaseRef(key, semaphore);
                        throw;
                    }
                }

                // Semaphore is being disposed, yield to allow disposal to complete before retry
                await Task.Yield();
            }
        }

        private void ReleaseRef(string key, RefCountedSemaphore semaphore)
        {
            if (semaphore.ReleaseRef() == 0)
            {
                // No more references - try to remove from dictionary atomically
                // Only remove if it's still the same instance (another thread might have replaced it)
                ((ICollection<System.Collections.Generic.KeyValuePair<string, RefCountedSemaphore>>)_locks)
                    .Remove(new System.Collections.Generic.KeyValuePair<string, RefCountedSemaphore>(key, semaphore));
                semaphore.Semaphore.Dispose();
            }
        }

        private sealed class RefCountedSemaphore
        {
            public SemaphoreSlim Semaphore { get; } = new(1, 1);
            private int _refCount;

            // Returns true if ref was added, false if semaphore is being disposed (refCount was 0)
            public bool TryAddRef()
            {
                while (true)
                {
                    int current = Volatile.Read(ref _refCount);
                    if (current < 0)
                        return false; // Marked for disposal

                    if (Interlocked.CompareExchange(ref _refCount, current + 1, current) == current)
                        return true;
                }
            }

            // Returns the new ref count after decrement. Marks as disposed when reaching 0.
            public int ReleaseRef()
            {
                while (true)
                {
                    int current = Volatile.Read(ref _refCount);
                    int newValue = current - 1;
                    if (newValue == 0)
                        newValue = -1; // Mark as disposed to prevent new refs

                    if (Interlocked.CompareExchange(ref _refCount, newValue, current) == current)
                        return current - 1; // Return the logical count (0 means no refs)
                }
            }
        }

        private sealed class Releaser : IDisposable
        {
            private readonly KeyedAsyncLock _parent;
            private readonly string _key;
            private readonly RefCountedSemaphore _semaphore;
            private int _disposed;

            public Releaser(KeyedAsyncLock parent, string key, RefCountedSemaphore semaphore)
            {
                _parent = parent;
                _key = key;
                _semaphore = semaphore;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

                _semaphore.Semaphore.Release();
                _parent.ReleaseRef(_key, _semaphore);
            }
        }
    }
}

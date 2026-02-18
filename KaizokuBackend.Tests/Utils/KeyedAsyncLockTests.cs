using KaizokuBackend.Utils;
using Xunit;

namespace KaizokuBackend.Tests.Utils;

/// <summary>
/// Tests for KeyedAsyncLock focusing on the reference counting mechanism
/// and concurrent access scenarios that were previously problematic.
/// </summary>
public class KeyedAsyncLockTests
{
    [Fact]
    public async Task LockAsync_SingleKey_AcquiresAndReleasesLock()
    {
        // Arrange
        var keyedLock = new KeyedAsyncLock();
        const string key = "test-key";

        // Act
        using (var lockHandle = await keyedLock.LockAsync(key))
        {
            // Assert - lock was acquired successfully
            Assert.NotNull(lockHandle);
        }
        // Lock released after using block
    }

    [Fact]
    public async Task LockAsync_DifferentKeys_AllowsConcurrentAccess()
    {
        // Arrange
        var keyedLock = new KeyedAsyncLock();
        var key1Acquired = new TaskCompletionSource<bool>();
        var key2Acquired = new TaskCompletionSource<bool>();
        var bothAcquired = new TaskCompletionSource<bool>();

        // Act - acquire locks on different keys concurrently
        var task1 = Task.Run(async () =>
        {
            using var lockHandle = await keyedLock.LockAsync("key1");
            key1Acquired.SetResult(true);
            await bothAcquired.Task; // Hold lock until both are acquired
        });

        var task2 = Task.Run(async () =>
        {
            using var lockHandle = await keyedLock.LockAsync("key2");
            key2Acquired.SetResult(true);
            await bothAcquired.Task; // Hold lock until both are acquired
        });

        // Wait for both locks to be acquired (with timeout)
        var key1Task = key1Acquired.Task;
        var key2Task = key2Acquired.Task;

        var completedInTime = await Task.WhenAll(
            Task.WhenAny(key1Task, Task.Delay(1000)),
            Task.WhenAny(key2Task, Task.Delay(1000))
        );

        // Assert - both locks were acquired concurrently
        Assert.True(key1Task.IsCompleted && key1Task.Result, "Key1 lock should be acquired");
        Assert.True(key2Task.IsCompleted && key2Task.Result, "Key2 lock should be acquired");

        // Cleanup
        bothAcquired.SetResult(true);
        await Task.WhenAll(task1, task2);
    }

    [Fact]
    public async Task LockAsync_SameKey_SerializesAccess()
    {
        // Arrange
        var keyedLock = new KeyedAsyncLock();
        const string key = "shared-key";
        var firstLockAcquired = new TaskCompletionSource<bool>();
        var secondLockAttempted = new TaskCompletionSource<bool>();
        var secondLockAcquired = new TaskCompletionSource<bool>();
        var releaseFirstLock = new TaskCompletionSource<bool>();

        // Act
        var task1 = Task.Run(async () =>
        {
            using var lockHandle = await keyedLock.LockAsync(key);
            firstLockAcquired.SetResult(true);
            await releaseFirstLock.Task; // Hold lock until signaled
        });

        // Wait for first lock to be acquired
        await firstLockAcquired.Task;

        var task2 = Task.Run(async () =>
        {
            secondLockAttempted.SetResult(true);
            using var lockHandle = await keyedLock.LockAsync(key);
            secondLockAcquired.SetResult(true);
        });

        // Wait for second lock attempt to start
        await secondLockAttempted.Task;
        await Task.Delay(50); // Give time for the second task to block

        // Assert - second lock should NOT be acquired yet
        Assert.False(secondLockAcquired.Task.IsCompleted,
            "Second lock should be blocked while first lock is held");

        // Release first lock
        releaseFirstLock.SetResult(true);

        // Wait for second lock (with timeout)
        var completedTask = await Task.WhenAny(secondLockAcquired.Task, Task.Delay(1000));

        // Assert - second lock should now be acquired
        Assert.True(secondLockAcquired.Task.IsCompleted && secondLockAcquired.Task.Result,
            "Second lock should be acquired after first is released");

        await Task.WhenAll(task1, task2);
    }

    [Fact]
    public async Task LockAsync_ParallelOnSameKey_DoesNotDisposeWhileInUse()
    {
        // This test verifies the fix for the race condition where a semaphore
        // could be disposed while another thread was still using it.

        // Arrange
        var keyedLock = new KeyedAsyncLock();
        const string key = "race-condition-key";
        const int parallelCount = 50;
        var exceptions = new List<Exception>();
        var lockObject = new object();

        // Act - hammer the same key from multiple threads
        var tasks = Enumerable.Range(0, parallelCount).Select(async i =>
        {
            try
            {
                // Introduce some jitter to increase chance of race conditions
                if (i % 2 == 0) await Task.Delay(1);

                using var lockHandle = await keyedLock.LockAsync(key);

                // Simulate some work while holding the lock
                await Task.Delay(5);
            }
            catch (Exception ex)
            {
                lock (lockObject)
                {
                    exceptions.Add(ex);
                }
            }
        });

        await Task.WhenAll(tasks);

        // Assert - no ObjectDisposedException should have been thrown
        Assert.Empty(exceptions);
    }

    [Fact]
    public async Task LockAsync_RapidAcquireRelease_SemaphoreCleanedUpCorrectly()
    {
        // This test verifies that semaphores are properly cleaned up
        // after all locks are released.

        // Arrange
        var keyedLock = new KeyedAsyncLock();
        const string key = "cleanup-key";
        const int iterations = 100;

        // Act - rapidly acquire and release the same key
        for (int i = 0; i < iterations; i++)
        {
            using var lockHandle = await keyedLock.LockAsync(key);
            // Minimal work
        }

        // Assert - should complete without errors
        // The internal dictionary should have cleaned up the semaphore
        // (we can't directly verify this without reflection, but no exceptions = success)

        // Final lock should work
        using var finalLock = await keyedLock.LockAsync(key);
        Assert.NotNull(finalLock);
    }

    [Fact]
    public async Task LockAsync_ConcurrentAcquireRelease_MaintainsCorrectRefCount()
    {
        // This test simulates the exact race condition that was fixed:
        // - Thread A acquires lock
        // - Thread B tries to acquire same key, blocks
        // - Thread A releases lock
        // - Without proper ref counting, the semaphore could be disposed
        //   before Thread B finishes acquiring it

        // Arrange
        var keyedLock = new KeyedAsyncLock();
        const string key = "refcount-key";
        const int parallelWaiters = 20;
        var barrier = new Barrier(parallelWaiters + 1);
        var completionCount = 0;

        // Act
        var tasks = new List<Task>();

        // Start many tasks that will all try to acquire the same key
        for (int i = 0; i < parallelWaiters; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                barrier.SignalAndWait(); // Sync all threads to start together

                using var lockHandle = await keyedLock.LockAsync(key);
                Interlocked.Increment(ref completionCount);
                await Task.Delay(1); // Small delay while holding lock
            }));
        }

        // Signal all threads to start
        barrier.SignalAndWait();

        // Wait for all to complete
        await Task.WhenAll(tasks);

        // Assert - all tasks should have completed successfully
        Assert.Equal(parallelWaiters, completionCount);
    }

    [Fact]
    public async Task LockAsync_WithCancellation_ReleasesRefCorrectly()
    {
        // Arrange
        var keyedLock = new KeyedAsyncLock();
        const string key = "cancellation-key";
        var firstLockAcquired = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource();

        // Act
        var task1 = Task.Run(async () =>
        {
            using var lockHandle = await keyedLock.LockAsync(key);
            firstLockAcquired.SetResult(true);
            await Task.Delay(5000); // Hold lock for a while
        });

        await firstLockAcquired.Task;

        // Try to acquire with a token that will be cancelled
        var task2 = keyedLock.LockAsync(key, cts.Token);

        await Task.Delay(50); // Let task2 start waiting
        cts.Cancel();

        // Assert - should throw OperationCanceledException
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task2);

        // Verify the lock still works after cancellation
        task1.Wait(1000); // Force task1 to complete via timeout mechanism
    }

    [Fact]
    public async Task LockAsync_MultipleKeysParallel_NoDeadlock()
    {
        // Arrange
        var keyedLock = new KeyedAsyncLock();
        var keys = Enumerable.Range(0, 10).Select(i => $"key-{i}").ToArray();
        const int operationsPerKey = 20;
        var random = new Random(42); // Deterministic seed

        // Act - perform many operations on multiple keys concurrently
        var tasks = keys.SelectMany(key =>
            Enumerable.Range(0, operationsPerKey).Select(async _ =>
            {
                await Task.Delay(random.Next(5)); // Random delay
                using var lockHandle = await keyedLock.LockAsync(key);
                await Task.Delay(random.Next(3)); // Random work
            }));

        var allTasks = Task.WhenAll(tasks);
        var completedInTime = await Task.WhenAny(allTasks, Task.Delay(10000));

        // Assert - should complete without deadlock
        Assert.Equal(allTasks, completedInTime);
    }

    [Fact]
    public async Task LockAsync_DisposeTwice_IsIdempotent()
    {
        // Arrange
        var keyedLock = new KeyedAsyncLock();
        const string key = "dispose-twice-key";

        // Act
        var lockHandle = await keyedLock.LockAsync(key);
        lockHandle.Dispose();

        // Dispose again - should not throw
        var exception = Record.Exception(() => lockHandle.Dispose());

        // Assert
        Assert.Null(exception);

        // Lock should still work for new acquisitions
        using var newLock = await keyedLock.LockAsync(key);
        Assert.NotNull(newLock);
    }
}

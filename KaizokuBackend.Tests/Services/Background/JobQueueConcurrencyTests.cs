using System.Collections.Concurrent;
using Xunit;

namespace KaizokuBackend.Tests.Services.Background;

/// <summary>
/// Tests for verifying thread safety of the job queue slot allocation mechanism.
/// These tests validate the concurrency fixes in JobQueueHostedService, specifically:
/// - MaxThreads limit enforcement under concurrent load
/// - Thread-safe add/remove operations on running jobs dictionary
/// - Proper slot allocation with lock protection
/// </summary>
public class JobQueueConcurrencyTests
{
    /// <summary>
    /// Simulates the slot allocation pattern used in JobQueueHostedService.
    /// This tests the concurrent dictionary + lock pattern that prevents over-allocation.
    /// </summary>
    private class SlotAllocator
    {
        private readonly ConcurrentDictionary<string, byte> _runningJobs = new();
        private readonly object _slotLock = new();
        private readonly int _maxSlots;

        public SlotAllocator(int maxSlots)
        {
            _maxSlots = maxSlots;
        }

        public int RunningCount => _runningJobs.Count;

        /// <summary>
        /// Attempts to allocate a slot for the given job ID.
        /// Uses the same pattern as JobQueueHostedService.ProcessQueueAsync.
        /// </summary>
        public bool TryAllocateSlot(string jobId)
        {
            lock (_slotLock)
            {
                if (_runningJobs.Count >= _maxSlots)
                    return false;

                return _runningJobs.TryAdd(jobId, 0);
            }
        }

        /// <summary>
        /// Releases a slot for the given job ID.
        /// </summary>
        public bool ReleaseSlot(string jobId)
        {
            return _runningJobs.TryRemove(jobId, out _);
        }
    }

    [Fact]
    public void TryAllocateSlot_SingleThread_RespectsMaxSlots()
    {
        // Arrange
        const int maxSlots = 5;
        var allocator = new SlotAllocator(maxSlots);

        // Act - allocate up to max
        for (int i = 0; i < maxSlots; i++)
        {
            var result = allocator.TryAllocateSlot($"job-{i}");
            Assert.True(result, $"Should allocate slot {i}");
        }

        // Try to allocate one more
        var overflowResult = allocator.TryAllocateSlot("job-overflow");

        // Assert
        Assert.False(overflowResult, "Should not allocate beyond max slots");
        Assert.Equal(maxSlots, allocator.RunningCount);
    }

    [Fact]
    public async Task TryAllocateSlot_ConcurrentAccess_NeverExceedsMaxSlots()
    {
        // This tests the race condition fix: without proper locking,
        // concurrent allocations could exceed the max slot limit.

        // Arrange
        const int maxSlots = 10;
        const int concurrentAttempts = 100;
        var allocator = new SlotAllocator(maxSlots);
        var barrier = new Barrier(concurrentAttempts);
        var successCount = 0;
        var maxObservedCount = 0;
        var countLock = new object();

        // Act - try to allocate from many threads simultaneously
        var tasks = Enumerable.Range(0, concurrentAttempts).Select(i => Task.Run(() =>
        {
            barrier.SignalAndWait(); // Sync all threads to start together

            if (allocator.TryAllocateSlot($"job-{i}"))
            {
                var currentCount = allocator.RunningCount;
                lock (countLock)
                {
                    successCount++;
                    if (currentCount > maxObservedCount)
                        maxObservedCount = currentCount;
                }
            }
        }));

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(maxSlots, successCount);
        Assert.True(maxObservedCount <= maxSlots,
            $"Running count ({maxObservedCount}) should never exceed max slots ({maxSlots})");
    }

    [Fact]
    public async Task AllocateAndRelease_ConcurrentOperations_MaintainsConsistency()
    {
        // Tests that concurrent add/remove operations maintain consistency

        // Arrange
        const int maxSlots = 20;
        const int iterations = 1000;
        var allocator = new SlotAllocator(maxSlots);
        var exceptions = new ConcurrentBag<Exception>();

        // Act - perform many allocate/release cycles concurrently
        var tasks = Enumerable.Range(0, iterations).Select(i => Task.Run(async () =>
        {
            try
            {
                var jobId = $"job-{i}";

                if (allocator.TryAllocateSlot(jobId))
                {
                    // Simulate some work
                    await Task.Delay(Random.Shared.Next(1, 5));

                    // Release the slot
                    var released = allocator.ReleaseSlot(jobId);
                    Assert.True(released, $"Should release allocated slot for {jobId}");
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }));

        await Task.WhenAll(tasks);

        // Assert
        Assert.Empty(exceptions);
        // After all operations, running count should be 0
        Assert.Equal(0, allocator.RunningCount);
    }

    [Fact]
    public async Task TryAllocateSlot_DuplicateJobId_RejectsSecondAttempt()
    {
        // Tests that the same job ID cannot be added twice

        // Arrange
        var allocator = new SlotAllocator(10);
        const string jobId = "duplicate-job";

        // Act
        var firstAttempt = allocator.TryAllocateSlot(jobId);
        var secondAttempt = allocator.TryAllocateSlot(jobId);

        // Assert
        Assert.True(firstAttempt, "First attempt should succeed");
        Assert.False(secondAttempt, "Second attempt with same ID should fail");
        Assert.Equal(1, allocator.RunningCount);
    }

    [Fact]
    public async Task ConcurrentAllocateRelease_UnderHighContention_NoExceptions()
    {
        // Stress test for the slot allocation mechanism

        // Arrange
        const int maxSlots = 5;
        const int threads = 50;
        const int operationsPerThread = 100;
        var allocator = new SlotAllocator(maxSlots);
        var exceptions = new ConcurrentBag<Exception>();
        var barrier = new Barrier(threads);

        // Act
        var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(async () =>
        {
            barrier.SignalAndWait();

            for (int i = 0; i < operationsPerThread; i++)
            {
                try
                {
                    var jobId = $"thread-{threadId}-job-{i}";

                    if (allocator.TryAllocateSlot(jobId))
                    {
                        // Verify we don't exceed max
                        var count = allocator.RunningCount;
                        if (count > maxSlots)
                        {
                            throw new InvalidOperationException(
                                $"Running count {count} exceeds max {maxSlots}");
                        }

                        await Task.Yield(); // Allow other threads to interleave
                        allocator.ReleaseSlot(jobId);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        }));

        await Task.WhenAll(tasks);

        // Assert
        Assert.Empty(exceptions);
    }

    /// <summary>
    /// Tests the pattern of checking count outside lock (optimization)
    /// followed by atomic allocation inside lock.
    /// </summary>
    [Fact]
    public async Task OptimisticCheckWithPessimisticAllocation_WorksCorrectly()
    {
        // This mirrors the actual pattern in JobQueueHostedService:
        // 1. Quick check outside lock (optimization)
        // 2. Re-check and allocate inside lock (correctness)

        // Arrange
        const int maxSlots = 3;
        var runningJobs = new ConcurrentDictionary<string, byte>();
        var slotLock = new object();
        var allocations = new ConcurrentBag<string>();
        var barrier = new Barrier(20);

        async Task<bool> TryAllocateWithOptimisticCheck(string jobId)
        {
            // Optimistic check (no lock)
            if (runningJobs.Count >= maxSlots)
                return false;

            // Pessimistic allocation (with lock)
            lock (slotLock)
            {
                if (runningJobs.Count >= maxSlots)
                    return false;

                return runningJobs.TryAdd(jobId, 0);
            }
        }

        // Act
        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            barrier.SignalAndWait();

            if (await TryAllocateWithOptimisticCheck($"job-{i}"))
            {
                allocations.Add($"job-{i}");
            }
        }));

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(maxSlots, allocations.Count);
        Assert.Equal(maxSlots, runningJobs.Count);
    }

    /// <summary>
    /// Tests that releasing a non-existent job ID is handled gracefully.
    /// </summary>
    [Fact]
    public void ReleaseSlot_NonExistentJob_ReturnsFalse()
    {
        // Arrange
        var allocator = new SlotAllocator(10);

        // Act
        var result = allocator.ReleaseSlot("non-existent-job");

        // Assert
        Assert.False(result);
        Assert.Equal(0, allocator.RunningCount);
    }

    /// <summary>
    /// Tests slot reuse after release.
    /// </summary>
    [Fact]
    public void SlotReuse_AfterRelease_WorksCorrectly()
    {
        // Arrange
        const int maxSlots = 2;
        var allocator = new SlotAllocator(maxSlots);

        // Act - fill slots
        Assert.True(allocator.TryAllocateSlot("job-1"));
        Assert.True(allocator.TryAllocateSlot("job-2"));
        Assert.False(allocator.TryAllocateSlot("job-3")); // Should fail

        // Release one
        Assert.True(allocator.ReleaseSlot("job-1"));

        // Now should be able to allocate
        Assert.True(allocator.TryAllocateSlot("job-3"));
        Assert.Equal(maxSlots, allocator.RunningCount);
    }

    /// <summary>
    /// Tests multiple queues scenario (each queue has its own dictionary).
    /// </summary>
    [Fact]
    public async Task MultipleQueues_IndependentSlotTracking()
    {
        // Arrange
        var queue1 = new SlotAllocator(maxSlots: 2);
        var queue2 = new SlotAllocator(maxSlots: 3);

        // Act
        queue1.TryAllocateSlot("q1-job-1");
        queue1.TryAllocateSlot("q1-job-2");
        queue2.TryAllocateSlot("q2-job-1");
        queue2.TryAllocateSlot("q2-job-2");
        queue2.TryAllocateSlot("q2-job-3");

        // Assert
        Assert.Equal(2, queue1.RunningCount);
        Assert.Equal(3, queue2.RunningCount);

        // Each queue should be at capacity
        Assert.False(queue1.TryAllocateSlot("q1-overflow"));
        Assert.False(queue2.TryAllocateSlot("q2-overflow"));
    }
}

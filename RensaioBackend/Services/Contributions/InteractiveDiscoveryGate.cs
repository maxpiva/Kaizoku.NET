namespace RensaioBackend.Services.Contributions;

public sealed class InteractiveDiscoveryGate
{
    private readonly object _lock = new();
    private int _active;
    private TaskCompletionSource _idle = CompletedSource();
    private TaskCompletionSource _nextActive = NewSource();

    public bool IsActive
    {
        get { lock (_lock) return _active > 0; }
    }

    public IDisposable Begin()
    {
        lock (_lock)
        {
            if (_active++ == 0)
            {
                _idle = NewSource();
                _nextActive.TrySetResult();
            }
        }
        return new Lease(this);
    }

    public Task WaitUntilIdleAsync(CancellationToken token = default)
    {
        Task task;
        lock (_lock) task = _active == 0 ? Task.CompletedTask : _idle.Task;
        return task.WaitAsync(token);
    }

    public Task WaitForActivityAsync(CancellationToken token = default)
    {
        Task task;
        lock (_lock) task = _active > 0 ? Task.CompletedTask : _nextActive.Task;
        return task.WaitAsync(token);
    }

    private void End()
    {
        lock (_lock)
        {
            if (_active <= 0)
                return;
            if (--_active == 0)
            {
                _idle.TrySetResult();
                _nextActive = NewSource();
            }
        }
    }

    private static TaskCompletionSource NewSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource CompletedSource()
    {
        var source = NewSource();
        source.SetResult();
        return source;
    }

    private sealed class Lease(InteractiveDiscoveryGate owner) : IDisposable
    {
        private InteractiveDiscoveryGate? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.End();
    }
}

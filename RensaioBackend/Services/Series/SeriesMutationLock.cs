using System.Collections.Concurrent;

namespace RensaioBackend.Services.Series;

// Shared by archive cleanup/verification, source settings and download publication.
// Keep gates alive: removing an idle gate races with callers already holding its reference.
public static class SeriesMutationLock
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    public static async Task<IDisposable> AcquireAsync(Guid seriesId, CancellationToken token = default)
    {
        var gate = Gates.GetOrAdd(seriesId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}

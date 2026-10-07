namespace RensaioBackend.Services.Downloads;

/// <summary>Cooperative cancellation only: native calls may finish, but cannot publish after pause.</summary>
public static class SeriesDownloadCancellation
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, HashSet<CancellationTokenSource>> Active = new();

    public static CancellationTokenSource Register(Guid seriesId, CancellationToken token)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (Gate)
        {
            if (!Active.TryGetValue(seriesId, out var sources))
                Active[seriesId] = sources = new();
            sources.Add(source);
        }
        return source;
    }

    public static void Unregister(Guid seriesId, CancellationTokenSource source)
    {
        lock (Gate)
        {
            if (Active.TryGetValue(seriesId, out var sources))
            {
                sources.Remove(source);
                if (sources.Count == 0) Active.Remove(seriesId);
            }
            source.Dispose();
        }
    }

    public static void Cancel(Guid seriesId)
    {
        lock (Gate)
        {
            if (!Active.TryGetValue(seriesId, out var sources)) return;
            foreach (var source in sources)
                source.Cancel();
        }
    }
}

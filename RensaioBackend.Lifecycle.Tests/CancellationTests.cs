using RensaioBackend.Services.Downloads;
using Xunit;

namespace RensaioBackend.Lifecycle.Tests;

public class CancellationTests
{
    [Fact]
    public void CancelOnlyTargetsCurrentWorkForSelectedSeries()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var first = SeriesDownloadCancellation.Register(id, default);
        var second = SeriesDownloadCancellation.Register(id, default);
        var unrelated = SeriesDownloadCancellation.Register(other, default);
        try
        {
            SeriesDownloadCancellation.Cancel(id);
            Assert.True(first.IsCancellationRequested);
            Assert.True(second.IsCancellationRequested);
            Assert.False(unrelated.IsCancellationRequested);
            var resumed = SeriesDownloadCancellation.Register(id, default);
            Assert.False(resumed.IsCancellationRequested);
            SeriesDownloadCancellation.Unregister(id, resumed);
        }
        finally
        {
            SeriesDownloadCancellation.Unregister(id, first);
            SeriesDownloadCancellation.Unregister(id, second);
            SeriesDownloadCancellation.Unregister(other, unrelated);
        }
    }

    [Fact]
    public async Task CompletionAndCancellationCanRaceWithoutDisposingActiveTokens()
    {
        var id = Guid.NewGuid();
        for (int i = 0; i < 200; i++)
        {
            var source = SeriesDownloadCancellation.Register(id, default);
            await Task.WhenAll(Task.Run(() => SeriesDownloadCancellation.Cancel(id)),
                Task.Run(() => SeriesDownloadCancellation.Unregister(id, source)));
        }
    }
}

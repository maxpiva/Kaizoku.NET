using Microsoft.EntityFrameworkCore;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Downloads;
using RensaioBackend.Services.Jobs.Models;
using Xunit;

namespace RensaioBackend.Lifecycle.Tests;

[Collection("Lifecycle")]
public class DeletionLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletePreservesExplicitPhysicalChoiceAndCancelsProducedWork(bool physical)
    {
        await using var f = new LifecycleFixture();
        await f.InitializeAsync();
        string path = Path.Combine(f.Scratch, f.Series.StoragePath);
        File.WriteAllText(Path.Combine(path, "chapter.cbz"), "fixture");
        File.WriteAllText(Path.Combine(path, "cover.jpg"), "fixture");
        await f.Downloads.QueueChapterDownloadsAsync(f.Provider, [f.Chapter()]);
        await f.Jobs.ScheduleRecurringJobAsync(JobType.GetChapters, f.Provider.Id, key: f.Provider.Id.ToString());
        await f.Jobs.EnqueueJobAsync(JobType.GetChapters, f.Provider.Id, key: f.Provider.Id.ToString());
        var active = SeriesDownloadCancellation.Register(f.Series.Id, default);
        try
        {
            await f.Commands.DeleteSeriesAsync(f.Series.Id, physical);
            Assert.True(active.IsCancellationRequested);
            Assert.Empty(await f.Db.Series.ToListAsync());
            Assert.Empty(await f.Db.SeriesProviders.ToListAsync());
            Assert.Empty(await f.Db.Queues.ToListAsync());
            Assert.Empty(await f.Db.Jobs.ToListAsync());
            Assert.Equal(!physical, Directory.Exists(path));
            Assert.Equal(JobResult.Delete, await f.Downloads.DownloadChapterAsync(f.Chapter(), null!));
        }
        finally { SeriesDownloadCancellation.Unregister(f.Series.Id, active); }
    }

    [Fact]
    public async Task RejectedPhysicalDeletionRetainsSeriesAndFiles()
    {
        await using var f = new LifecycleFixture();
        await f.InitializeAsync();
        string path = Path.Combine(f.Scratch, f.Series.StoragePath);
        File.WriteAllText(Path.Combine(path, "chapter.cbz"), "fixture");
        Directory.CreateSymbolicLink(Path.Combine(path, "link"), f.Scratch);
        await Assert.ThrowsAsync<IOException>(() => f.Commands.DeleteSeriesAsync(f.Series.Id, true));
        Assert.Equal(1, await f.Db.Series.CountAsync());
        Assert.Equal(1, await f.Db.SeriesProviders.CountAsync());
        Assert.True(File.Exists(Path.Combine(path, "chapter.cbz")));
    }

    [Fact]
    public async Task FilesystemIoFailureRetainsDatabaseRecord()
    {
        await using var f = new LifecycleFixture();
        await f.InitializeAsync();
        string path = Path.Combine(f.Scratch, f.Series.StoragePath);
        Directory.Delete(path);
        File.WriteAllText(path, "not a directory");
        await Assert.ThrowsAsync<IOException>(() => f.Commands.DeleteSeriesAsync(f.Series.Id, true));
        Assert.Equal(1, await f.Db.Series.CountAsync());
        Assert.True(File.Exists(path));
    }
}

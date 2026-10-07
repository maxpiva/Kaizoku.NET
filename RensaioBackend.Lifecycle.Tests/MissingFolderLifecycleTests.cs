using Microsoft.EntityFrameworkCore;
using RensaioBackend.Models;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Downloads;
using RensaioBackend.Services.Jobs.Models;
using Xunit;

namespace RensaioBackend.Lifecycle.Tests;

[Collection("Lifecycle")]
public class MissingFolderLifecycleTests
{
    [Fact]
    public async Task ReaderFolderRemovalPausesBeforeNativeRefreshCancelsQueueAndAllowsExplicitResume()
    {
        await using var f = new LifecycleFixture();
        await f.InitializeAsync();
        var path = Path.Combine(f.Scratch, f.Series.StoragePath);
        File.WriteAllText(Path.Combine(path, "chapter.cbz"), "scratch archive");
        f.Provider.Chapters.Add(new Chapter { Number = 1, Filename = "chapter.cbz", DownloadDate = DateTime.UtcNow });
        f.Db.Entry(f.Provider).Property(p => p.Chapters).IsModified = true;
        await f.Db.SaveChangesAsync();
        await f.Jobs.ScheduleRecurringJobAsync(JobType.GetChapters, f.Provider.Id, key: f.Provider.Id.ToString());
        await f.Downloads.QueueChapterDownloadsAsync(f.Provider, [f.Chapter(2)]);
        var active = SeriesDownloadCancellation.Register(f.Series.Id, default);
        try
        {
            Directory.Delete(path, true); // reader deletes only the scratch folder
            Assert.Equal(JobResult.Success, await f.Commands.GetChaptersAsync(f.Provider.Id));
            Assert.True((await f.Db.Series.AsNoTracking().SingleAsync()).PauseDownloads);
            Assert.False(Directory.Exists(path));
            Assert.Empty(await f.Db.Queues.ToListAsync());
            Assert.False((await f.Db.Jobs.SingleAsync()).IsEnabled);
            Assert.True(active.IsCancellationRequested);
            Assert.Equal(JobResult.Delete, await f.Downloads.QueueChapterDownloadsAsync(f.Provider, [f.Chapter(3)]));
            await f.Commands.UpdateSeriesAsync(f.Update(false));
            Assert.True(Directory.Exists(path));
            Assert.False((await f.Db.Series.AsNoTracking().SingleAsync()).PauseDownloads);
            await f.Downloads.QueueChapterDownloadsAsync(f.Provider, [f.Chapter(4)]);
            Assert.Single(await f.Db.Queues.ToListAsync());
        }
        finally { SeriesDownloadCancellation.Unregister(f.Series.Id, active); }
    }

    [Fact]
    public async Task DetectionFromActiveDownloadFinishesCleanupEvenWhenItCancelsItsOwnToken()
    {
        await using var f = new LifecycleFixture();
        await f.InitializeAsync();
        f.Provider.Chapters.Add(new Chapter { Number = 1, Filename = "chapter.cbz" });
        f.Db.Entry(f.Provider).Property(p => p.Chapters).IsModified = true;
        await f.Db.SaveChangesAsync();
        await f.Downloads.QueueChapterDownloadsAsync(f.Provider, [f.Chapter(2)]);
        await f.Jobs.ScheduleRecurringJobAsync(JobType.GetChapters, f.Provider.Id, key: f.Provider.Id.ToString());
        var active = SeriesDownloadCancellation.Register(f.Series.Id, default);
        try
        {
            Directory.Delete(Path.Combine(f.Scratch, f.Series.StoragePath), true);
            Assert.True(await f.Downloads.PauseIfSeriesFolderMissingAsync(f.Series.Id, active.Token));
            Assert.True(active.IsCancellationRequested);
            Assert.Empty(await f.Db.Queues.ToListAsync());
            Assert.False((await f.Db.Jobs.SingleAsync()).IsEnabled);
        }
        finally { SeriesDownloadCancellation.Unregister(f.Series.Id, active); }
    }

    [Fact]
    public async Task FirstSubscriptionWithoutDownloadEvidenceDoesNotAutoPause()
    {
        await using var f = new LifecycleFixture();
        await f.InitializeAsync();
        Directory.Delete(Path.Combine(f.Scratch, f.Series.StoragePath));
        Assert.False(await f.Downloads.PauseIfSeriesFolderMissingAsync(f.Series.Id));
        await f.Downloads.QueueChapterDownloadsAsync(f.Provider, [f.Chapter()]);
        Assert.Single(await f.Db.Queues.ToListAsync());
        Assert.False((await f.Db.Series.AsNoTracking().SingleAsync()).PauseDownloads);
    }

    [Fact]
    public async Task IndividualChapterRemovalDoesNotChangeCompletionistMode()
    {
        await using var f = new LifecycleFixture();
        await f.InitializeAsync();
        f.Provider.Chapters.Add(new Chapter { Number = 1, Filename = "missing.cbz" });
        f.Db.Entry(f.Provider).Property(p => p.Chapters).IsModified = true;
        await f.Db.SaveChangesAsync();
        Assert.False(await f.Downloads.PauseIfSeriesFolderMissingAsync(f.Series.Id));
        Assert.False((await f.Db.Series.AsNoTracking().SingleAsync()).PauseDownloads);
    }
}

using Microsoft.EntityFrameworkCore;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Downloads;
using RensaioBackend.Services.Jobs.Models;
using Xunit;

namespace RensaioBackend.Lifecycle.Tests;

[Collection("Lifecycle")]
public class PauseLifecycleTests
{
    [Fact]
    public async Task PauseCancelsProducedQueueBlocksLateEnqueueAndRetryThenResumeAllowsQueue()
    {
        await using var f = new LifecycleFixture();
        await f.InitializeAsync();
        await f.Jobs.ScheduleRecurringJobAsync(JobType.GetChapters, f.Provider.Id, key: f.Provider.Id.ToString());
        await f.Downloads.QueueChapterDownloadsAsync(f.Provider, [f.Chapter()]);
        Assert.Equal(1, await f.Db.Queues.CountAsync());
        var active = SeriesDownloadCancellation.Register(f.Series.Id, default);
        try
        {
            await f.Commands.UpdateSeriesAsync(f.Update(true));
            Assert.True(active.IsCancellationRequested);
            Assert.True((await f.Db.Series.AsNoTracking().SingleAsync()).PauseDownloads);
            Assert.False((await f.Db.Jobs.SingleAsync()).IsEnabled);
            Assert.Empty(await f.Db.Queues.ToListAsync());
            Assert.Equal(JobResult.Delete, await f.Downloads.QueueChapterDownloadsAsync(f.Provider, [f.Chapter(2)]));
            Assert.Equal(JobResult.Delete, await f.Downloads.DownloadChapterAsync(f.Chapter(), null!));
            var retryId = await f.Jobs.EnqueueJobAsync(JobType.Download, f.Chapter(), key: "old retry", extraKey: f.Series.Id.ToString());
            await f.Downloads.ManageErrorDownloadAsync(retryId, ErrorDownloadAction.Retry);
            Assert.Equal(0, (await f.Db.Queues.AsNoTracking().SingleAsync()).RetryCount);
            Assert.Equal(1, await f.Db.Queues.CountAsync()); // no new retry entry
            await f.Commands.UpdateSeriesAsync(f.Update(false));
            Assert.False((await f.Db.Series.AsNoTracking().SingleAsync()).PauseDownloads);
            Assert.True((await f.Db.Jobs.SingleAsync()).IsEnabled);
            await f.Downloads.QueueChapterDownloadsAsync(f.Provider, [f.Chapter(3)]);
            Assert.Equal(2, await f.Db.Queues.CountAsync());
        }
        finally { SeriesDownloadCancellation.Unregister(f.Series.Id, active); }
    }

    [Fact]
    public async Task LateQueueCompletionAndFailureTolerateRemovedJob()
    {
        await using var f = new LifecycleFixture();
        await f.InitializeAsync();
        await f.Downloads.QueueChapterDownloadsAsync(f.Provider, [f.Chapter()]);
        var queued = await f.Db.Queues.SingleAsync();
        await f.Jobs.CancelDownloadsForSeriesAsync(f.Series.Id);
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, f.Jobs);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var queue = new RensaioBackend.Services.Background.JobQueueHostedService(
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(provider),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RensaioBackend.Services.Background.JobQueueHostedService>.Instance,
            new RensaioBackend.Services.Jobs.Settings.JobsSettings());
        var methods = typeof(RensaioBackend.Services.Background.JobQueueHostedService);
        await (Task)methods.GetMethod("HandleJobResultAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(queue, [queued, JobResult.Delete, JobQueues.Downloads, CancellationToken.None])!;
        await (Task)methods.GetMethod("HandleJobFailureAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(queue, [queued, new RensaioBackend.Services.Jobs.Settings.JobsSettings().GetQueueSettings()[1], CancellationToken.None])!;
        Assert.Empty(await f.Db.Queues.ToListAsync());
    }

    [Fact]
    public async Task DeletedOrDisabledProviderCannotStartOrQueueChapterWork()
    {
        await using var f = new LifecycleFixture();
        await f.InitializeAsync();
        f.Provider.IsDisabled = true;
        await f.Db.SaveChangesAsync();
        Assert.Equal(JobResult.Delete, await f.Downloads.DownloadChapterAsync(f.Chapter(), null!));
        Assert.Equal(JobResult.Delete, await f.Downloads.QueueChapterDownloadsAsync(f.Provider, [f.Chapter()]));
        Assert.Empty(await f.Db.Queues.ToListAsync());
    }
}

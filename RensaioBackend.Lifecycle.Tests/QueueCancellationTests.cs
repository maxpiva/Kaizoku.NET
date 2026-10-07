using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RensaioBackend.Data;
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Downloads;
using RensaioBackend.Services.Jobs;
using RensaioBackend.Services.Jobs.Settings;
using Xunit;

namespace RensaioBackend.Lifecycle.Tests;

public class QueueCancellationTests
{
    [Fact]
    public async Task CancelRemovesProducedWaitingRunningFailedAndCompletedJobsOnlyForSelectedSeries()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var management = new JobManagementService(db, new JobsSettings(), null!,
            NullLogger<JobManagementService>.Instance);
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var active = SeriesDownloadCancellation.Register(id, default);
        try
        {
            foreach (var status in Enum.GetValues<QueueStatus>())
            {
                var job = await management.EnqueueJobAsync(JobType.Download, id,
                    key: status.ToString(), extraKey: id.ToString(), queue: "Downloads");
                await db.Queues.Where(q => q.Id == job).ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, status));
            }
            await management.EnqueueJobAsync(JobType.Download, other, key: "other", extraKey: other.ToString());
            await management.EnqueueJobAsync(JobType.GetChapters, id, key: "metadata", extraKey: id.ToString());
            Assert.Equal(Enum.GetValues<QueueStatus>().Length, await management.CancelDownloadsForSeriesAsync(id));
            Assert.True(active.IsCancellationRequested);
            Assert.Equal(2, await db.Queues.CountAsync());
            Assert.False(await db.Queues.AnyAsync(q => q.JobType == JobType.Download && q.ExtraKey == id.ToString()));
        }
        finally { SeriesDownloadCancellation.Unregister(id, active); }
    }
}

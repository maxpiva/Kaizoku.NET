using KaizokuBackend.Data;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Extensions;
using KaizokuBackend.Services.Jobs.Settings;
using KaizokuBackend.Services.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Services.Downloads
{
    /// <summary>
    /// Service for download query operations following CQRS pattern
    /// </summary>
    public class DownloadQueryService
    {
        private readonly AppDbContext _db;
        private readonly JobsSettings _jobSettings;
        private readonly ILogger<DownloadQueryService> _logger;

        public DownloadQueryService(AppDbContext db, JobsSettings jobSettings, ILogger<DownloadQueryService> logger)
        {
            _db = db;
            _jobSettings = jobSettings;
            _logger = logger;
        }

        /// <summary>
        /// Gets download information for a specific series
        /// </summary>
        /// <param name="seriesId">Series identifier</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of download information for the series</returns>
        public async Task<List<DownloadInfoDto>> GetDownloadsForSeriesAsync(Guid seriesId, CancellationToken token = default)
        {
            string extraKey = seriesId.ToString();
            List<EnqueueEntity> result = await _db.Queues.Where(a => a.JobType == JobType.Download && a.ExtraKey == extraKey).ToListAsync(token);
            return result.Select(a=>a.ToDownloadInfo()).Where(a => a != null).OrderBy(a => a!.ScheduledDateUTC).ToList()!;
        }

        /// <summary>
        /// Gets download metrics including counts by status
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>Download metrics</returns>
        public async Task<DownloadsMetricsDto> GetDownloadsMetricsAsync(CancellationToken token = default)
        {
            DownloadsMetricsDto dm = new DownloadsMetricsDto();
            dm.Downloads = await _db.Queues.CountAsync(a => a.JobType == JobType.Download && a.Status == QueueStatus.Running, token).ConfigureAwait(false);
            dm.Queued = await _db.Queues.CountAsync(a => a.JobType == JobType.Download && a.Status == QueueStatus.Waiting, token).ConfigureAwait(false);
            dm.Failed = await _db.Queues.CountAsync(a => a.JobType == JobType.Download && a.Status == QueueStatus.Failed, token).ConfigureAwait(false);
            return dm;
        }

        private static Expression<Func<T, bool>> CombineAnd<T>(
            Expression<Func<T, bool>> expr1,
            Expression<Func<T, bool>> expr2)
        {
            var parameter = Expression.Parameter(typeof(T));

            var body = Expression.AndAlso(
                Expression.Invoke(expr1, parameter),
                Expression.Invoke(expr2, parameter));

            return Expression.Lambda<Func<T, bool>>(body, parameter);
        }


        /// <summary>
        /// Gets downloads by status with pagination
        /// </summary>
        /// <param name="status">Queue status to filter by</param>
        /// <param name="maxCount">Maximum number of downloads to return</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of downloads with the specified status</returns>
        public async Task<DownloadInfoListDto> GetDownloadsAsync(QueueStatus status, int maxCount, string? keyword, CancellationToken token = default)
        {
            DownloadInfoListDto ls = new DownloadInfoListDto();
            Expression<Func<EnqueueEntity, bool>> where = a => a.JobType == JobType.Download && a.Status == status;
            if (keyword != null)
                where = a => a.JobType == JobType.Download && a.Status == status && a.JobParameters!.Contains(keyword);
            ls.TotalCount = await _db.Queues.CountAsync(where, token);
            List<EnqueueEntity> result = [];
            
            switch (status)
            {
                case QueueStatus.Running:
                case QueueStatus.Failed:
                    result = await _db.Queues.Where(where).OrderBy(a => a.ScheduledDate).Take(maxCount).ToListAsync(token);
                    break;
                case QueueStatus.Completed:
                    result = await _db.Queues.Where(where).OrderByDescending(a => a.FinishedDate).Take(maxCount).ToListAsync(token);
                    break;
                case QueueStatus.Waiting:
                    DateTime now = DateTime.UtcNow;
                    Expression<Func<EnqueueEntity, bool>> where2 = CombineAnd(where, a => a.ScheduledDate <= now);
                    result = await GetEnqueueForAsync(where2, maxCount, token);
                    if (result.Count < maxCount)
                    {

                        // If we have less than maxCount, we can add more from the waiting queue
                        where2 = CombineAnd(where, a => a.ScheduledDate > now);
                        int remaining = maxCount - result.Count;
                        List<EnqueueEntity> additional = await GetEnqueueForAsync(where2, remaining, token);
                        result.AddRange(additional);
                    }
                    break;
            }
            
            ls.Downloads = result.Select(a=>a.ToDownloadInfo()).Where(a => a != null).ToList()!;
            return ls;
        }

        

        /// <summary>
        /// Gets enqueued jobs with fair sharing and priority ordering
        /// </summary>
        /// <param name="where">Filter expression</param>
        /// <param name="maxCount">Maximum count to return</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>List of enqueued jobs</returns>
        private async Task<List<EnqueueEntity>> GetEnqueueForAsync(Expression<Func<EnqueueEntity, bool>> where, int maxCount, CancellationToken token = default)
        {
            QueueSettings queueEntry = _jobSettings.GetQueueSettings().First(a => a.Name == JobQueues.Downloads);
            var maxGroupLimit = queueEntry.MaxPerGroup;
            Dictionary<string, int> counts = await _db.Queues.Where(where).GroupBy(a => a.GroupKey).ToDictionaryAsync(a => a.Key, a => a.Count(), token);
            
            // Find waiting jobs for this queue
            var jobs = await _db.Queues
                .Where(where)
                .OrderByDescending(j => j.Priority).ThenBy(a => a.ScheduledDate).ToListAsync(token).ConfigureAwait(false);
            
            Dictionary<Priority, List<EnqueueEntity>> jobsByPriority = jobs
                .GroupBy(j => j.Priority)
                .ToDictionary(g => g.Key, g => g.ToList());
            
            foreach (Priority p in jobsByPriority.Keys)
            {
                Dictionary<string, List<EnqueueEntity>> prin = jobsByPriority[p]
                    .GroupBy(a => a.GroupKey)
                    .ToDictionary(g => g.Key, g => g.Take(counts.GetLocalGroupMax(g.Key, 500)).ToList());
                jobsByPriority[p] = prin.SelectMany(a => a.Value).FairShareOrderBy(a => a.GroupKey).ToList();
            }
            
            return jobsByPriority.SelectMany(a => a.Value).Take(maxCount).ToList();
        }
    }
}
using KaizokuBackend.Models;
using KaizokuBackend.Services.Jobs.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace KaizokuBackend.Services.Jobs.Settings
{
    public class JobsSettings
    {
        private List<QueueSettings> _queueThreadLimits = new List<QueueSettings>()
        {
            new QueueSettings(JobQueues.Default, 10, 150),
            new QueueSettings(JobQueues.Downloads, 10, 150)
        };

        public List<QueueSettings> GetQueueSettings()
        {
            return _queueThreadLimits.ToList();
        }
        public void SetQueueSettings(JobQueues queue, int maxThreads, int retries, int maxPerGroup, TimeSpan? span = null)
        {
            QueueSettings settings = _queueThreadLimits.First(a => a.Name == queue);
            settings.MaxThreads = maxThreads;
            settings.MaxRetries = retries;
            settings.MaxPerGroup = maxPerGroup;
            if (span!=null)
                settings.RetryTimeSpan = span.Value;
        }

        public TimeSpan QueuePollingInterval { get; set; } = TimeSpan.FromMilliseconds(500);
        public TimeSpan JobsPollingInterval { get; set; } = TimeSpan.FromMilliseconds(500);

        public Dictionary<JobType, TimeSpan> JobTimes = new Dictionary<JobType, TimeSpan>()
        {
            { JobType.UpdateExtensions, TimeSpan.FromHours(1)},
            { JobType.GetChapters, TimeSpan.FromHours(2)},
            { JobType.GetLatest, TimeSpan.FromMinutes(30)},
            { JobType.DailyUpdate,TimeSpan.FromDays(1)}

        };
    }
}

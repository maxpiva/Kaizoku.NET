using KaizokuBackend.Services.Jobs.Models;

namespace KaizokuBackend.Services.Jobs.Settings;

public class QueueSettings
{
    public QueueSettings(JobQueues name, int maxThreads = 10, int maxRetries = 150, TimeSpan? retryTimeSpan = null)
    {
        Name = name;
        MaxThreads = maxThreads;
        MaxRetries = maxRetries;
        RetryTimeSpan = retryTimeSpan ?? TimeSpan.FromMinutes(5);
        MaxPerGroup = 3;
    }
    public JobQueues Name { get; set; }
    public int MaxThreads { get; set; }
    public int MaxRetries { get; set; }
    public TimeSpan RetryTimeSpan { get; set; }
    public int MaxPerGroup { get; set; }
}
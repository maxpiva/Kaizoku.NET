using KaizokuBackend.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace KaizokuBackend.Migration.Models
{
    public class Enqueue
    {
        [Key] 
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Queue { get; set; } = "";
        public JobType JobType { get; set; } = JobType.ScanLocalFiles;
        public string? JobParameters { get; set; }
        public string Key { get; set; } = "";
        public string GroupKey { get; set; } = "";
        public string? ExtraKey { get; set; }

        public QueueStatus Status { get; set; } = QueueStatus.Waiting;
        public Priority Priority { get; set; } = Priority.Low;
        public DateTime EnqueuedDate { get; set; }
        public DateTime? StartedDate { get; set; }
        public DateTime ScheduledDate { get; set; }
        public DateTime? FinishedDate { get; set; }
        public int RetryCount { get; set; }


    }
}

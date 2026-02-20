using KaizokuBackend.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace KaizokuBackend.Models.Database;

public class JobEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public JobType JobType { get; set; }
    public string? JobParameters { get; set; } = "";
    public string Key { get; set; } = "";
    public string GroupKey { get; set; } = "";
    public TimeSpan TimeBetweenJobs { get; set; }
    public int MinutePlace { get; set; }
    public bool IsEnabled { get; set; } = true;
    public Priority Priority { get; set; } = Priority.Low;
    public DateTime? PreviousExecution { get; set; }
    public DateTime NextExecution { get; set; }
}
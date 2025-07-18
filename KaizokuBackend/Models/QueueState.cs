using System.Text.Json.Serialization;
using KaizokuBackend.Models.Database;

namespace KaizokuBackend.Models;

public class QueueState
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("jobType")]
    public JobType JobType { get; set; }
    [JsonPropertyName("queue")]
    public string Queue { get; set; } = "";
    [JsonPropertyName("parameters")]
    public string? Parameters { get; set; }
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";
    [JsonPropertyName("priority")]
    public Priority Priority { get; set; }
    [JsonPropertyName("status")]
    public QueueStatus Status { get; set; }
    [JsonPropertyName("enqueuedDate")]
    public DateTime EnqueuedDate { get; set; }
    [JsonPropertyName("startedDate")]
    public DateTime? StartedDate { get; set; }
    [JsonPropertyName("scheduledDate")]
    public DateTime ScheduledDate { get; set; }
    [JsonPropertyName("finishedDate")]
    public DateTime? FinishedDate { get; set; }
    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; }


}
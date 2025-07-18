using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class ProgressState
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("jobType")]
    public JobType JobType { get; set; }

    [JsonPropertyName("parameter")]
    public object? Parameter { get; set; } = null;


    [JsonPropertyName("progressStatus")]
    public ProgressStatus ProgressStatus { get; set; }

    [JsonPropertyName("percentage")]
    public decimal Percentage { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}
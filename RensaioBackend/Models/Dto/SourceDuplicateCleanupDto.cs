using System.Text.Json.Serialization;

namespace RensaioBackend.Models.Dto;

public sealed class SourceDuplicateCleanupRequestDto
{
    [JsonPropertyName("confirmed")]
    public bool Confirmed { get; set; }
}

public sealed class SourceDuplicateCleanupResultDto
{
    [JsonPropertyName("deleted")]
    public int Deleted { get; set; }
    [JsonPropertyName("skipped")]
    public int Skipped { get; set; }
}

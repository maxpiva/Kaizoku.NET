using System.Text.Json.Serialization;

namespace RensaioBackend.Models.Dto;

public class SeriesIntegrityResultDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    [JsonPropertyName("badFiles")]
    public List<ArchiveIntegrityResultDto> BadFiles { get; set; } = [];
    [JsonPropertyName("seriesName")]
    public string SeriesName { get; set; }
    [JsonPropertyName("seriesPath")]
    public string SeriesPath { get; set; }


}
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto;

public class SeriesIntegrityResultDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    [JsonPropertyName("badFiles")]
    public List<ArchiveIntegrityResultDto> BadFiles { get; set; } = [];
}
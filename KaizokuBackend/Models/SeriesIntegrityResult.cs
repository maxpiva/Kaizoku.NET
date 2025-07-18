using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class SeriesIntegrityResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    [JsonPropertyName("badFiles")]
    public List<ArchiveIntegrityResult> BadFiles { get; set; } = [];
}
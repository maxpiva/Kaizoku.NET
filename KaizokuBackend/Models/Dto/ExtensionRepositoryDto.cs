using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto;

public class ExtensionRepositoryDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("id")]
    public string Id { get; set; }
    [JsonPropertyName("entries")]
    public List<ExtensionEntryDto> Entries { get; set; } = [];
}

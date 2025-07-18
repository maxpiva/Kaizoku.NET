using KaizokuBackend.Models.Database;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class SuwayomiSource
{
    [JsonPropertyName("id")]
    [Key]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")]

    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("lang")]
  public string Lang { get; set; } = string.Empty;
    [JsonPropertyName("iconUrl")]
  public string IconUrl { get; set; } = string.Empty;
    [JsonPropertyName("supportsLatest")]
  public bool SupportsLatest { get; set; }
    [JsonPropertyName("isConfigurable")]
  public bool IsConfigurable { get; set; }
    [JsonPropertyName("isNsfw")]
    public bool IsNsfw { get; set; }
    [JsonPropertyName("displayName")]
  public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("series")]
  public ICollection<Series> Series { get; set; } = new List<Series>();
}
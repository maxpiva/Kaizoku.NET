using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class ProviderPreferences
{
    [JsonPropertyName("apkName")]
    public required string ApkName { get; set; }
    [JsonPropertyName("preferences")]
    public List<ProviderPreference> Preferences { get; set; } = [];
}
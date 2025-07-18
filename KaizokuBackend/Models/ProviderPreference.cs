using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class ProviderPreference
{
    [JsonPropertyName("type")]
    public EntryType Type { get; set; }
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
    [JsonPropertyName("summary")]
    public string? Summary { get; set; } = "";
    [JsonPropertyName("valueType")]
    public ValueType ValueType { get; set; }
    [JsonPropertyName("defaultValue")]
    public object? DefaultValue { get; set; }
    [JsonPropertyName("entries")]
    public List<string>? Entries { get; set; }
    [JsonPropertyName("entryValues")]
    public List<string>? EntryValues { get; set; }
    [JsonPropertyName("currentValue")]
    public object? CurrentValue { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; } = null;
}
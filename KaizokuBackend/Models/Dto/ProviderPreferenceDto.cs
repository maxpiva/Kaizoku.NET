using KaizokuBackend.Models.Enums;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto;

public class ProviderPreferenceDto
{
    [JsonPropertyName("type")]
    public EntryType Type { get; set; }
    [JsonPropertyName("index")]
    public int Index { get; set; } = -1;
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
    [JsonPropertyName("summary")]
    public string? Summary { get; set; } = "";
    [JsonPropertyName("valueType")]
    public Enums.ValueType ValueType { get; set; }
    [JsonPropertyName("defaultValue")]
    public object? DefaultValue { get; set; }
    [JsonPropertyName("entries")]
    public List<string>? Entries { get; set; }
    [JsonPropertyName("entryValues")]
    public List<string>? EntryValues { get; set; }
    [JsonPropertyName("currentValue")]
    public object? CurrentValue { get; set; }

    [JsonPropertyName("languages")]
    public string[] Languages { get; set; } = [];
}
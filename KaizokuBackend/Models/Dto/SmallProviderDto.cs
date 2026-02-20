using System.Text.Json.Serialization;
using KaizokuBackend.Models;

namespace KaizokuBackend.Models.Dto;

public class SmallProviderDto : ProviderSummaryBase
{
    [JsonPropertyName("url")]
    public override string? Url { get; set; } = string.Empty;
}

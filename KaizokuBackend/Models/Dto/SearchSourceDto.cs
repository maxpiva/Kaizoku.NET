using System.Text.Json.Serialization;
using KaizokuBackend.Models;

namespace KaizokuBackend.Models.Dto;

public class SearchSourceDto : ProviderSummaryBase
{
    [JsonPropertyName("mihonProviderId")]
    public string MihonProviderId { get; set; } = string.Empty;

    [JsonPropertyName("Name")]
    public string Name
    {
        get => Title;
        set => Title = value;
    }
}
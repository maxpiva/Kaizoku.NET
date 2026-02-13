using System.Text.Json.Serialization;
using KaizokuBackend.Models;

namespace KaizokuBackend.Models.Dto;

public class MatchInfoDto : ProviderSummaryBase
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }
}
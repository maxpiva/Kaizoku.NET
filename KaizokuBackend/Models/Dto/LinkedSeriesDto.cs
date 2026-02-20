using KaizokuBackend.Models;
using KaizokuBackend.Models.Abstractions;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto;
// [Schema] // Controller I/O Model
public class LinkedSeriesDto : SeriesSummaryBase
{
    public string ProviderId { get; set; } = "";
    [JsonPropertyName("linkedIds")]
    public List<string> LinkedIds { get; set; } = new List<string>();


}
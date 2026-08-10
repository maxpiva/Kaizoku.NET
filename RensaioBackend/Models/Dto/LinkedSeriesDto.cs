using RensaioBackend.Models;
using RensaioBackend.Models.Abstractions;
using System.Text.Json.Serialization;

namespace RensaioBackend.Models.Dto;
// [Schema] // Controller I/O Model
public class LinkedSeriesDto : SeriesSummaryBase
{
    public string ProviderId { get; set; } = "";
    [JsonPropertyName("linkedIds")]
    public List<string> LinkedIds { get; set; } = new List<string>();

    /// <summary>
    /// Fuzzy relevance of this result's title against the search keyword (0-100, TitleMatcher).
    /// Lets the client keep one unified, relevance-ordered list when results arrive from
    /// multiple channels (installed search + streamed discovery results).
    /// </summary>
    [JsonPropertyName("relevance")]
    public int Relevance { get; set; }
}
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class ProviderSeriesOption : SeriesProviderDetailsBase
{
    [JsonPropertyName("lastChapter")]
    public decimal? LastChapter { get; set; }

    [JsonPropertyName("preferred")]
    public bool Preferred { get; set; }
}

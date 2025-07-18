using System.Text.Json.Serialization;

namespace KaizokuBackend.Models
{
    public class SeriesInfo : BaseSeriesInfo
    {
        
        [JsonPropertyName("providers")]
        public List<SmallProviderInfo> Providers { get; set; } = [];
    }
}

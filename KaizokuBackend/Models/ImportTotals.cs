using System.Text.Json.Serialization;

namespace KaizokuBackend.Models
{
    public class ImportTotals
    {
        [JsonPropertyName("totalSeries")]
        public int TotalSeries { get; set; }
        [JsonPropertyName("totalProviders")]
        public int TotalProviders { get; set; }
        [JsonPropertyName("totalDownloads")]
        public int TotalDownloads { get; set; }
    }
}
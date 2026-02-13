using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto
{
    public class SeriesInfoDto : BaseSeriesDto
    {
        
        [JsonPropertyName("providers")]
        public List<SmallProviderDto> Providers { get; set; } = [];
    }
}

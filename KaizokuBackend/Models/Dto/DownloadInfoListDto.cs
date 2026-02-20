using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto
{

    public class DownloadInfoListDto
    {
        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; } = 0;

        [JsonPropertyName("downloads")]
        public List<DownloadInfoDto> Downloads { get; set; } = [];
    }
}
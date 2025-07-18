using System.Text.Json.Serialization;

namespace KaizokuBackend.Models
{

    public class DownloadInfoList
    {
        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; } = 0;

        [JsonPropertyName("downloads")]
        public List<DownloadInfo> Downloads { get; set; } = [];
    }
}
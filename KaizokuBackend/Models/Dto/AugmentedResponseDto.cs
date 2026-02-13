using System.Text.Json.Serialization;
using KaizokuBackend.Models.Enums;
using KaizokuBackend.Models;
using Action = KaizokuBackend.Models.Action;

namespace KaizokuBackend.Models.Dto
{
    public class AugmentedResponseDto : ImportSummaryBase
    {
        [JsonPropertyName("storageFolderPath")]
        public string StorageFolderPath
        {
            get => NormalizedPath;
            set => NormalizedPath = value;
        }

        [JsonPropertyName("useCategoriesForPath")]
        public bool UseCategoriesForPath { get; set; }

        [JsonPropertyName("existingSeries")]
        public bool ExistingSeries { get; set; }

        [JsonPropertyName("existingSeriesId")]
        public Guid? ExistingSeriesId { get; set; }
        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = [];
        [JsonPropertyName("series")]
        public List<ProviderSeriesDetails> Series { get; set; } = [];
        [JsonPropertyName("preferredLanguages")]
        public List<string> PreferredLanguages { get; set; } = [];

        [JsonPropertyName("disableJobs")]
        public bool DisableJobs { get; set; } = false;

        [JsonPropertyName("startChapter")]
        public decimal? StartChapter { get; set; } = null;

        [JsonIgnore] 
        public ImportSeriesSnapshot LocalInfo { get; set; } = new ImportSeriesSnapshot();
        [JsonIgnore]
        public ImportStatus Status { get; set; }
        [JsonIgnore]
        public Action Action { get; set; }

    }
}

using System.Text.Json.Serialization;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models.Database;
using Action = KaizokuBackend.Models.Database.Action;

namespace KaizokuBackend.Models
{
    public class AugmentedResponse
    {
        private string _storageFolderPath = string.Empty;

        [JsonPropertyName("storageFolderPath")]
        public string StorageFolderPath
        {
            get => _storageFolderPath.SanitizeDirectory();
            set => _storageFolderPath = value;
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
        public List<FullSeries> Series { get; set; } = [];
        [JsonPropertyName("preferredLanguages")]
        public List<string> PreferredLanguages { get; set; } = [];

        [JsonPropertyName("disableJobs")]
        public bool DisableJobs { get; set; } = false;

        [JsonPropertyName("startChapter")]
        public decimal? StartChapter { get; set; } = null;

        [JsonIgnore] 
        public KaizokuInfo LocalInfo { get; set; } = new KaizokuInfo();
        [JsonIgnore]
        public ImportStatus Status { get; set; }
        [JsonIgnore]
        public Action Action { get; set; }

    }
}
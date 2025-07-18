using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models.Database;

namespace KaizokuBackend.Models
{
    public class KaizokuInfo
    {
        
        public string Title { get; set; } = "";
        public SeriesStatus Status { get; set; } = SeriesStatus.UNKNOWN;
        public string Artist { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Genre { get; set; } = [];
        public string Type { get; set; } = "";
        public int ChapterCount { get; set; }
        public DateTime? LastUpdatedUTC { get; set; }
        public List<ProviderInfo> Providers { get; set; } = [];
        public bool IdDisabled { get; set; } 
        public int KaizokuVersion { get; set; } = 1;


        private string _path = string.Empty;

        public string Path
        {
            get => _path.SanitizeDirectory();
            set => _path = value;
        }

        [JsonIgnore]
        public ArchiveCompare ArchiveCompare { get; set; } = ArchiveCompare.Equal;
        [JsonIgnore]
        public Guid? MatchExisting { get; set; }
    }
}

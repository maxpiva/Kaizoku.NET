using KaizokuBackend.Extensions;
using KaizokuBackend.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace KaizokuBackend.Migration.Models
{
    public class Import
    {
        private string _path = string.Empty;


        [Key]
        public required string Path
        {
            get => _path.SanitizeDirectory();
            set => _path = value;
        }
        public required string Title { get; set; }
        public ImportStatus Status { get; set; } = ImportStatus.Import;
        public KaizokuBackend.Models.Action Action { get; set; } = KaizokuBackend.Models.Action.Add;
        public required KaizokuBackend.Models.ImportSeriesSnapshot Info { get; set; }
        public List<ProviderSeriesDetails>? Series { get; set; }

        public decimal? ContinueAfterChapter { get; set; } = null;


    }
}

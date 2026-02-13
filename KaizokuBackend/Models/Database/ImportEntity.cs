using System.ComponentModel.DataAnnotations;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Models.Database
{
    public class ImportEntity
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
        public Action Action { get; set; } = Action.Add;
        public required ImportSeriesSnapshot Info { get; set; }
        public List<ProviderSeriesDetails>? Series { get; set; }

        public decimal? ContinueAfterChapter { get; set; } = null;


    }
}


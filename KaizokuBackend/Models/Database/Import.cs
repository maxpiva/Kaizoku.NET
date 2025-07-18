using System.ComponentModel.DataAnnotations;
using KaizokuBackend.Extensions;

namespace KaizokuBackend.Models.Database
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
        public Action Action { get; set; } = Action.Add;
        public required KaizokuInfo Info { get; set; }
        public List<FullSeries>? Series { get; set; }

        public decimal? ContinueAfterChapter { get; set; } = null;


    }
}


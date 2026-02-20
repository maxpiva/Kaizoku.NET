using System.ComponentModel.DataAnnotations;

namespace KaizokuBackend.Migration.Models
{
    public class EtagCache
    {
        [Key]
        public string Key { get; set; } = "";

        public string Etag { get; set; } = "";
        public DateTime LastUpdated { get; set; }

    }
}

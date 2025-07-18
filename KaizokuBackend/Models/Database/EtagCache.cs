using System.ComponentModel.DataAnnotations;

namespace KaizokuBackend.Models.Database
{
    public class EtagCache
    {
        [Key]
        public string Key { get; set; } = "";

        public string Etag { get; set; } = "";
        public DateTime LastUpdated { get; set; }

    }
}

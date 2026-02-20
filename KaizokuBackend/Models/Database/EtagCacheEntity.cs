using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace KaizokuBackend.Models.Database
{
    [Index(nameof(Url))]
    [Index(nameof(NextUpdateUTC))]
    public class EtagCacheEntity
    {
        [Key]
        public string Key { get; set; } = "";
        public string Url { get; set; } = "";
        public string ExternalEtag { get; set; } = "";

        public string Etag { get; set; } = "";
        public string ContentType { get; set; } = "";
        public string Extension { get; set; } = "";
        public string? MihonProviderId { get; set; }
        public DateTime NextUpdateUTC { get; set; }

    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Models.Database
{
    public class MangaRequestEntity
    {
        [Key]
        public Guid Id { get; set; }
        public Guid RequestedByUserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? ProviderData { get; set; }
        public RequestStatus Status { get; set; } = RequestStatus.Pending;
        public Guid? ReviewedByUserId { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewNote { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(RequestedByUserId))]
        public virtual UserEntity? RequestedByUser { get; set; }

        [ForeignKey(nameof(ReviewedByUserId))]
        public virtual UserEntity? ReviewedByUser { get; set; }
    }
}

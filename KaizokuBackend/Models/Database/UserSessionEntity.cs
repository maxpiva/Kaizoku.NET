using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KaizokuBackend.Models.Database
{
    public class UserSessionEntity
    {
        [Key]
        public Guid Id { get; set; }

        public Guid UserId { get; set; }
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public bool IsRevoked { get; set; } = false;

        [ForeignKey(nameof(UserId))]
        public virtual UserEntity? User { get; set; }
    }
}

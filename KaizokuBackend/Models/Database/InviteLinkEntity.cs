using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KaizokuBackend.Models.Database
{
    public class InviteLinkEntity
    {
        [Key]
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public Guid CreatedByUserId { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int MaxUses { get; set; } = 1;
        public int UsedCount { get; set; } = 0;
        public Guid? PermissionPresetId { get; set; }
        public bool IsActive { get; set; } = true;

        [ForeignKey(nameof(CreatedByUserId))]
        public virtual UserEntity? CreatedByUser { get; set; }

        [ForeignKey(nameof(PermissionPresetId))]
        public virtual PermissionPresetEntity? PermissionPreset { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KaizokuBackend.Models.Database
{
    public class PermissionPresetEntity
    {
        [Key]
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid CreatedByUserId { get; set; }
        public bool IsDefault { get; set; } = false;

        public bool CanViewLibrary { get; set; } = true;
        public bool CanRequestSeries { get; set; } = true;
        public bool CanAddSeries { get; set; } = false;
        public bool CanEditSeries { get; set; } = false;
        public bool CanDeleteSeries { get; set; } = false;
        public bool CanManageDownloads { get; set; } = false;
        public bool CanViewQueue { get; set; } = true;
        public bool CanBrowseSources { get; set; } = true;
        public bool CanViewNSFW { get; set; } = false;
        public bool CanManageRequests { get; set; } = false;
        public bool CanManageJobs { get; set; } = false;
        public bool CanViewStatistics { get; set; } = true;

        [ForeignKey(nameof(CreatedByUserId))]
        public virtual UserEntity? CreatedByUser { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KaizokuBackend.Models.Database
{
    public class UserPermissionEntity
    {
        [Key]
        [ForeignKey(nameof(User))]
        public Guid UserId { get; set; }

        public bool CanViewLibrary { get; set; } = true;
        public bool CanRequestSeries { get; set; } = true;
        public bool CanAddSeries { get; set; } = false;
        public bool CanEditSeries { get; set; } = false;
        public bool CanDeleteSeries { get; set; } = false;
        public bool CanManageDownloads { get; set; } = false;
        public bool CanViewQueue { get; set; } = false;
        public bool CanBrowseSources { get; set; } = false;
        public bool CanViewNSFW { get; set; } = false;
        public bool CanManageRequests { get; set; } = false;
        public bool CanManageJobs { get; set; } = false;
        public bool CanViewStatistics { get; set; } = false;

        public virtual UserEntity? User { get; set; }
    }
}

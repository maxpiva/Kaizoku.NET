using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto.Auth
{
    public class PermissionDto
    {
        [JsonPropertyName("canViewLibrary")]
        public bool CanViewLibrary { get; set; } = true;

        [JsonPropertyName("canRequestSeries")]
        public bool CanRequestSeries { get; set; } = true;

        [JsonPropertyName("canAddSeries")]
        public bool CanAddSeries { get; set; } = false;

        [JsonPropertyName("canEditSeries")]
        public bool CanEditSeries { get; set; } = false;

        [JsonPropertyName("canDeleteSeries")]
        public bool CanDeleteSeries { get; set; } = false;

        [JsonPropertyName("canManageDownloads")]
        public bool CanManageDownloads { get; set; } = false;

        // Defaults must match UserPermissionEntity defaults (false) to prevent
        // silent permission escalation when a DTO is deserialized with missing fields.
        [JsonPropertyName("canViewQueue")]
        public bool CanViewQueue { get; set; } = false;

        [JsonPropertyName("canBrowseSources")]
        public bool CanBrowseSources { get; set; } = false;

        [JsonPropertyName("canViewNSFW")]
        public bool CanViewNSFW { get; set; } = false;

        [JsonPropertyName("canManageRequests")]
        public bool CanManageRequests { get; set; } = false;

        [JsonPropertyName("canManageJobs")]
        public bool CanManageJobs { get; set; } = false;

        [JsonPropertyName("canViewStatistics")]
        public bool CanViewStatistics { get; set; } = false;
    }

    public class UpdatePermissionDto : PermissionDto
    {
    }
}

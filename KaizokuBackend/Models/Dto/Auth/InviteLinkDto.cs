using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto.Auth
{
    public class InviteLinkDto
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("createdByUserId")]
        public Guid CreatedByUserId { get; set; }

        [JsonPropertyName("createdByUsername")]
        public string CreatedByUsername { get; set; } = string.Empty;

        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }

        [JsonPropertyName("maxUses")]
        public int MaxUses { get; set; }

        [JsonPropertyName("usedCount")]
        public int UsedCount { get; set; }

        [JsonPropertyName("permissionPresetId")]
        public Guid? PermissionPresetId { get; set; }

        [JsonPropertyName("permissionPresetName")]
        public string? PermissionPresetName { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
    }

    public class InviteValidationDto
    {
        [JsonPropertyName("isValid")]
        public bool IsValid { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("permissionPresetName")]
        public string? PermissionPresetName { get; set; }
    }

    public class CreateInviteDto
    {
        [JsonPropertyName("expiresInDays")]
        public int ExpiresInDays { get; set; } = 7;

        [JsonPropertyName("maxUses")]
        public int MaxUses { get; set; } = 1;

        [JsonPropertyName("permissionPresetId")]
        public Guid? PermissionPresetId { get; set; }
    }
}

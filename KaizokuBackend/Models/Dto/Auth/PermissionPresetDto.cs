using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto.Auth
{
    public class PermissionPresetDto
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("isDefault")]
        public bool IsDefault { get; set; }

        [JsonPropertyName("createdByUserId")]
        public Guid CreatedByUserId { get; set; }

        [JsonPropertyName("permissions")]
        public PermissionDto Permissions { get; set; } = new();
    }

    public class CreatePresetDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("permissions")]
        public PermissionDto Permissions { get; set; } = new();
    }

    public class UpdatePresetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("permissions")]
        public PermissionDto? Permissions { get; set; }
    }
}

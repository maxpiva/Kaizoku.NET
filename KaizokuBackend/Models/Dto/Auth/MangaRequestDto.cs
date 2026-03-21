using System.Text.Json.Serialization;
using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Models.Dto.Auth
{
    public class MangaRequestDto
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("requestedByUserId")]
        public Guid RequestedByUserId { get; set; }

        [JsonPropertyName("requestedByUsername")]
        public string RequestedByUsername { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("thumbnailUrl")]
        public string? ThumbnailUrl { get; set; }

        [JsonPropertyName("providerData")]
        public string? ProviderData { get; set; }

        [JsonPropertyName("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RequestStatus Status { get; set; }

        [JsonPropertyName("reviewedByUserId")]
        public Guid? ReviewedByUserId { get; set; }

        [JsonPropertyName("reviewedByUsername")]
        public string? ReviewedByUsername { get; set; }

        [JsonPropertyName("reviewedAt")]
        public DateTime? ReviewedAt { get; set; }

        [JsonPropertyName("reviewNote")]
        public string? ReviewNote { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }
    }

    public class CreateRequestDto
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("thumbnailUrl")]
        public string? ThumbnailUrl { get; set; }

        [JsonPropertyName("providerData")]
        public string? ProviderData { get; set; }
    }

    public class ApproveRequestDto
    {
        [JsonPropertyName("seriesData")]
        public AugmentedResponseDto? SeriesData { get; set; }

        [JsonPropertyName("reviewNote")]
        public string? ReviewNote { get; set; }
    }

    public class DenyRequestDto
    {
        [JsonPropertyName("reviewNote")]
        public string? ReviewNote { get; set; }
    }
}

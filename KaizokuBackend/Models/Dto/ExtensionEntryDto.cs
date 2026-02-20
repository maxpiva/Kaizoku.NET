using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto;

public class ExtensionEntryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; }
    [JsonPropertyName("onlineRepositoryName")]
    public string OnlineRepositoryName { get; set; }
    [JsonPropertyName("onlineRepositoryId")]
    public string OnlineRepositoryId { get; set; }
    [JsonPropertyName("isLocal")]
    public bool IsLocal { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("downloadUTC")]
    public DateTimeOffset DownloadUTC { get; set; }
    [JsonPropertyName("package")]
    public string Package { get; set; }
    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("nsfw")]
    public bool Nsfw { get; set; }

    [JsonPropertyName("sources")]
    public List<ExtensionSourceDto> Sources { get; set; } = [];



}

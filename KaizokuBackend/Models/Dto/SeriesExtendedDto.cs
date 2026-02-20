using System.Text.Json.Serialization;
using KaizokuBackend.Extensions;

namespace KaizokuBackend.Models.Dto;

public class SeriesExtendedDto : BaseSeriesDto
{
    [JsonPropertyName("providers")]
    public List<ProviderExtendedDto> Providers { get; set; } = [];

    [JsonPropertyName("chapterList")]
    public string ChapterList { get; set; } = string.Empty;

    private string _path = string.Empty;
    [JsonPropertyName("path")]
    public string Path
    {
        get => _path.SanitizeDirectory();
        set => _path = value;
    }
}
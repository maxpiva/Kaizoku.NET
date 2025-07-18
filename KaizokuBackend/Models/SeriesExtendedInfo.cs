using System.Text.Json.Serialization;
using KaizokuBackend.Extensions;

namespace KaizokuBackend.Models;

public class SeriesExtendedInfo : BaseSeriesInfo
{
    [JsonPropertyName("providers")]
    public List<ProviderExtendedInfo> Providers { get; set; } = [];

    [JsonPropertyName("chapterList")]
    public string ChapterList { get; set; } = string.Empty;

    [JsonPropertyName("pauseDownloads")]
    public bool PauseDownloads { get; set; } = false;


    private string _path = string.Empty;
    [JsonPropertyName("path")]
    public string Path
    {
        get => _path.SanitizeDirectory();
        set => _path = value;
    }
}
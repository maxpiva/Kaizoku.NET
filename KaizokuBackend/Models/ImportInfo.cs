using KaizokuBackend.Models.Database;
using System.Text.Json.Serialization;
using KaizokuBackend.Extensions;
using Action = KaizokuBackend.Models.Database.Action;

namespace KaizokuBackend.Models;

public class ImportInfo
{
    private string _path = string.Empty;

    [JsonPropertyName("path")]
    public required string Path
    {
        get => _path.SanitizeDirectory();
        set => _path = value;
    }
    [JsonPropertyName("title")]
    public required string Title { get; set; }
    [JsonPropertyName("status")]
    public ImportStatus Status { get; set; } = ImportStatus.Import;
    [JsonPropertyName("continueAfterChapter")]
    public decimal? ContinueAfterChapter { get; set; }
    [JsonPropertyName("action")]
    public Action Action { get; set; }
    [JsonPropertyName("series")]
    public List<SmallSeries>? Series { get; set; } = [];

}
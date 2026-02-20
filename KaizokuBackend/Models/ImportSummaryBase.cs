using KaizokuBackend.Extensions;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

/// <summary>
/// Provides shared sanitized path handling for import-related models.
/// </summary>
public abstract class ImportSummaryBase
{
    private string _path = string.Empty;

    [JsonIgnore]
    protected string NormalizedPath
    {
        get => _path.SanitizeDirectory();
        set => _path = value ?? string.Empty;
    }
}

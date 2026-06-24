using System.Text.Json.Serialization;

namespace RensaioBackend.Models.Dto;

/// <summary>
/// Result of a series rename operation: renaming the series folder to match the
/// current title and renaming each downloaded .cbz to the canonical naming scheme.
/// </summary>
public class SeriesRenameResultDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>True when the series folder was renamed on disk.</summary>
    [JsonPropertyName("folderRenamed")]
    public bool FolderRenamed { get; set; }

    /// <summary>Relative storage path before the rename.</summary>
    [JsonPropertyName("oldFolder")]
    public string OldFolder { get; set; } = string.Empty;

    /// <summary>Relative storage path after the rename (unchanged if folder was not renamed).</summary>
    [JsonPropertyName("newFolder")]
    public string NewFolder { get; set; } = string.Empty;

    /// <summary>Number of .cbz archives renamed to the canonical scheme.</summary>
    [JsonPropertyName("filesRenamed")]
    public int FilesRenamed { get; set; }

    /// <summary>Number of archives that could not be renamed (e.g. target name already taken).</summary>
    [JsonPropertyName("filesFailed")]
    public int FilesFailed { get; set; }

    /// <summary>Optional human-readable note (e.g. why the folder rename was skipped).</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

using System.Text.RegularExpressions;

namespace RensaioBackend.Extensions;

public static class SeriesModelExtensions
{
    /// <summary>
    /// Default fallback leaf name when a sanitized segment would otherwise be empty.
    /// </summary>
    public const string DefaultFolderName = "Unknown";

    public static string NormalizeStoragePath(string? path)
    {
        return (path ?? string.Empty).SanitizeDirectory();
    }

    /// <summary>
    /// Validates and sanitizes a user-supplied relative storage path for a series.
    /// <list type="bullet">
    /// <item>Rejects absolute/rooted paths, network (UNC) paths, drive-letter paths, and path
    /// traversal ("..") — a series path must always live inside the configured storage folder.</item>
    /// <item>Rejects empty/null paths.</item>
    /// <item>Each segment is made folder-name-safe (<see cref="string.MakeFolderNameSafe"/>) so
    /// illegal filename characters for the host OS are neutralized, never passed through.</item>
    /// <item>Normalizes separators to the host OS (<see cref="string.SanitizeDirectory"/>) and
    /// strips leading/trailing separators so the result is a clean relative path.</item>
    /// </list>
    /// </summary>
    /// <param name="path">The relative path requested by the user.</param>
    /// <returns>A safe relative storage path (no leading/trailing separators).</returns>
    /// <exception cref="ArgumentException">When the path is empty, rooted, absolute, UNC, or contains traversal.</exception>
    public static string SanitizeAndValidateStoragePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Series storage path must not be empty.");

        var raw = path.Trim();

        // Reject anything that escapes the storage folder or references a machine/drive root.
        if (raw.StartsWith("/") || raw.StartsWith("\\") || raw.StartsWith("\\\\"))
            throw new ArgumentException("Series storage path must be relative to the storage folder (no leading slash).");
        if (raw.Length >= 2 && (raw[1] == ':' ))
            throw new ArgumentException("Series storage path must be relative to the storage folder (no drive letter).");
        if (Regex.IsMatch(raw, @"^(?:[A-Za-z]:[\\/])", RegexOptions.IgnoreCase))
            throw new ArgumentException("Series storage path must be relative to the storage folder (no drive letter).");

        // Split on both separator styles, drop empties (repeated/trailing separators), then
        // reject traversal before doing any sanitization work.
        var segments = raw.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
        if (segments.Any(s => s == "." || s == ".."))
            throw new ArgumentException("Series storage path must not contain '.' or '..' segments.");

        // Sanitize each segment for the host OS and rebuild the relative path.
        var safeSegments = segments.Select(s => s.Trim().MakeFolderNameSafe()).ToList();
        string joined = string.Join(System.IO.Path.DirectorySeparatorChar.ToString(), safeSegments);
        return NormalizeStoragePath(joined).Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string? ValidateDisplayName(string? name)
    {
        if (name == null)
            return null;
        if (name.Any(char.IsControl))
            throw new ArgumentException("Series display name must not contain control characters.");
        name = name.Trim();
        if (name.Length == 0 || name.Length > 250)
            throw new ArgumentException("Series display name must contain 1 to 250 characters.");
        return name;
    }

    public static int ClampChapterCount(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)value;
    }

    public static int ClampChapterCount(int value)
    {
        return ClampChapterCount((long)value);
    }
}

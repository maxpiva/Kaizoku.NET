using System.Text.RegularExpressions;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using SettingsModel = KaizokuBackend.Models.Settings;

namespace KaizokuBackend.Services.Naming;

/// <summary>
/// Parses and validates file/folder naming templates
/// </summary>
public class TemplateParser : ITemplateParser
{
    // Regex to match {Variable} or {Variable:format}
    private static readonly Regex VariablePattern = new(@"\{(\w+)(?::([^}]+))?\}", RegexOptions.Compiled);

    // Variables allowed in file name templates
    private static readonly HashSet<string> FileNameVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "Series", "Chapter", "Volume", "Provider", "Scanlator",
        "Language", "Title", "Year", "Month", "Day"
    };

    // Variables allowed in folder path templates
    private static readonly HashSet<string> FolderPathVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "Series", "Type", "Provider", "Language", "Year"
    };

    // Sample data for previews
    private static readonly TemplateVariables SampleVariables = new(
        Series: "One Piece",
        Chapter: 1089.5m,
        Volume: 105,
        Provider: "MangaDex",
        Scanlator: "TCBScans",
        Language: "en",
        Title: "The Beginning",
        UploadDate: new DateTime(2024, 6, 15),
        Type: "Manga",
        MaxChapter: 1200m
    );

    /// <inheritdoc/>
    public string ParseFileName(string template, TemplateVariables vars, SettingsModel settings)
    {
        string result = ExpandTemplate(template, vars, settings, isFileName: true);
        result = result.ReplaceInvalidFilenameAndPathCharacters();
        result = Regex.Replace(result, @"\s+", " ").Trim();
        return result;
    }

    /// <inheritdoc/>
    public string ParseFolderPath(string template, TemplateVariables vars, SettingsModel settings)
    {
        string result = ExpandTemplate(template, vars, settings, isFileName: false);
        // Process each path segment separately
        var segments = result.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var sanitizedSegments = segments.Select(s => s.ReplaceInvalidFilenameAndPathCharacters().Trim());
        return Path.Combine(sanitizedSegments.ToArray());
    }

    /// <inheritdoc/>
    public TemplateValidationResult ValidateTemplate(string template, TemplateType type)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var usedVariables = new List<string>();
        var allowedVariables = type == TemplateType.FileName ? FileNameVariables : FolderPathVariables;

        if (string.IsNullOrWhiteSpace(template))
        {
            errors.Add("Template cannot be empty");
            return new TemplateValidationResult(false, errors, warnings, usedVariables);
        }

        var matches = VariablePattern.Matches(template);
        foreach (Match match in matches)
        {
            string varName = match.Groups[1].Value;
            usedVariables.Add(varName);

            if (!allowedVariables.Contains(varName))
            {
                errors.Add($"Unknown variable: {{{varName}}}. Allowed: {string.Join(", ", allowedVariables.Select(v => $"{{{v}}}"))}");
            }
        }

        // Check for recommended variables
        if (type == TemplateType.FileName)
        {
            if (!usedVariables.Contains("Series", StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add("Template is missing {Series} - filenames may be ambiguous");
            }
            if (!usedVariables.Contains("Chapter", StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add("Template is missing {Chapter} - filenames may collide");
            }
        }

        if (type == TemplateType.FolderPath)
        {
            if (!usedVariables.Contains("Series", StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add("Template is missing {Series} - folder structure may be unclear");
            }
        }

        return new TemplateValidationResult(errors.Count == 0, errors, warnings, usedVariables);
    }

    /// <inheritdoc/>
    public string GetPreview(string template, TemplateType type)
    {
        var sampleSettings = new SettingsModel
        {
            CategorizedFolders = true
        };

        return type == TemplateType.FileName
            ? ParseFileName(template, SampleVariables, sampleSettings)
            : ParseFolderPath(template, SampleVariables, sampleSettings);
    }

    private string ExpandTemplate(string template, TemplateVariables vars, SettingsModel settings, bool isFileName)
    {
        return VariablePattern.Replace(template, match =>
        {
            string varName = match.Groups[1].Value;
            string? format = match.Groups[2].Success ? match.Groups[2].Value : null;

            return GetVariableValue(varName, format, vars, settings, isFileName);
        });
    }

    private string GetVariableValue(string varName, string? format, TemplateVariables vars, SettingsModel settings, bool isFileName)
    {
        return varName.ToLowerInvariant() switch
        {
            "series" => SanitizeForTemplate(vars.Series),
            "chapter" => FormatChapter(vars.Chapter, format, vars.MaxChapter, settings),
            "volume" => FormatVolume(vars.Volume, format, settings),
            "provider" => FormatProvider(vars.Provider, vars.Scanlator),
            "scanlator" => SanitizeForTemplate(vars.Scanlator ?? ""),
            "language" => vars.Language.ToLowerInvariant(),
            "title" => FormatTitle(vars.Title),
            "year" => vars.UploadDate?.Year.ToString() ?? "",
            "month" => vars.UploadDate?.Month.ToString("D2") ?? "",
            "day" => vars.UploadDate?.Day.ToString("D2") ?? "",
            "type" => SanitizeForTemplate(vars.Type ?? "Manga"),
            _ => $"{{{varName}}}" // Keep unknown variables as-is
        };
    }

    private static string SanitizeForTemplate(string value)
    {
        // Remove characters that cause issues in file/folder names
        return value.Replace("(", "").Replace(")", "").Trim();
    }

    private static string FormatProvider(string provider, string? scanlator)
    {
        string result = provider.Replace("-", "_");
        if (!string.IsNullOrEmpty(scanlator) && provider != scanlator)
        {
            result += "-" + scanlator;
        }
        return result.Replace("[", "(").Replace("]", ")");
    }

    private static string FormatChapter(decimal? chapter, string? format, decimal? maxChapter, SettingsModel settings)
    {
        if (!chapter.HasValue)
            return "";

        // Determine padding length from format string (e.g., "000" = 3 digits)
        // If no format specified, no padding is applied
        int paddingLength = 0;
        if (!string.IsNullOrEmpty(format) && format.All(c => c == '0'))
        {
            paddingLength = format.Length;
        }

        // Format chapter with proper decimal handling
        if (chapter.Value % 1 != 0)
        {
            // Decimal chapter (e.g., 5.5) - pad integer part only
            int intPart = (int)chapter.Value;
            decimal decPart = chapter.Value - intPart;
            string intStr = paddingLength > 0 ? intPart.ToString().PadLeft(paddingLength, '0') : intPart.ToString();
            return intStr + decPart.ToString(System.Globalization.CultureInfo.InvariantCulture).Substring(1);
        }
        else
        {
            // Whole number chapter
            string intStr = ((int)chapter.Value).ToString();
            return paddingLength > 0 ? intStr.PadLeft(paddingLength, '0') : intStr;
        }
    }

    private static string FormatVolume(int? volume, string? format, SettingsModel settings)
    {
        if (!volume.HasValue)
            return "";

        string volumeStr = volume.Value.ToString();

        // Determine padding from format string (e.g., "00" = 2 digits)
        // If no format specified, no padding is applied
        int paddingLength = 0;
        if (!string.IsNullOrEmpty(format) && format.All(c => c == '0'))
        {
            paddingLength = format.Length;
        }

        return paddingLength > 0 ? volumeStr.PadLeft(paddingLength, '0') : volumeStr;
    }

    private static string FormatTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "";

        string trimmed = title.Trim();

        // Skip if title is just chapter info
        string lower = trimmed.ToLowerInvariant();
        if (lower.Contains("ch.") || lower.Contains("chapter") || lower.Contains("chap"))
            return "";

        return "(" + trimmed.Replace('(', '[').Replace(')', ']') + ")";
    }

    private static string FormatDecimal(decimal value)
    {
        return value % 1 == 0
            ? ((int)value).ToString()
            : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

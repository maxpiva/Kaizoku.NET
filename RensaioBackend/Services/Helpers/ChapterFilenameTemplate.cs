using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RensaioBackend.Services.Helpers;

/// <summary>Opt-in archive names. Empty templates retain canonical filename-only recovery.</summary>
public static class ChapterFilenameTemplate
{
    private static readonly Regex Token = new(@"\{(series|chapter|title|source|language)(:0{1,8})?\}");
    public static string? Validate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return null;
        if (template.Length > 160) return "Use at most 160 characters in the filename template.";
        var matches = Token.Matches(template);
        if (!matches.Any(m => m.Groups[1].Value == "chapter")) return "Include {chapter} or {chapter:0000} in the template.";
        if (matches.Any(m => m.Groups[2].Success && m.Groups[1].Value != "chapter")) return "Only {chapter} supports zero padding (1–8 zeros).";
        var literal = Token.Replace(template, "");
        if (literal.Any(c => char.IsControl(c) || "{}./\\:*?\"<>|".Contains(c)))
            return "Use supported tokens and filename text only: no paths, dots, extensions, or unknown tokens.";
        return null;
    }

    public static string Render(string template, string series, decimal? chapter, string? title,
        string source, string language, string sourceId, string? scanlator, string? chapterUrl)
    {
        var error = Validate(template);
        if (error != null || string.IsNullOrWhiteSpace(template)) throw new ArgumentException(error ?? "Template is empty.");
        var number = chapter?.ToString("0.############################", CultureInfo.InvariantCulture) ?? "unknown";
        var rendered = Token.Replace(template, m => m.Groups[1].Value switch
        {
            "series" => series,
            "title" => title ?? "",
            "source" => source,
            "language" => language,
            "chapter" => chapter.HasValue && m.Groups[2].Success
                ? chapter.Value.ToString(m.Groups[2].Value[1..] + ".############################", CultureInfo.InvariantCulture)
                : number,
            _ => throw new InvalidOperationException()
        });
        // Length-prefixed fields prevent ambiguous concatenation; source/language/URL distinguish
        // archives even when sanitization, title truncation, or identical chapter numbers collide.
        var fields = new[] { sourceId, source, language, scanlator ?? "", number, chapterUrl ?? "" };
        var identity = string.Concat(fields.Select(f => f.Length + ":" + f));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
        var stem = Safe(rendered);
        // Do not start with the canonical [source] grammar: the recovery wizard would otherwise
        // guess the wrong series/chapter from a custom template. rensaio.json remains authoritative.
        stem = stem.TrimStart('[', ']', '.', ' ');
        if (stem.Length == 0) stem = "Chapter";
        var suffix = $" [{Safe(source)}][{Safe(language)}]-{hash}.cbz";
        suffix = TruncateUtf8(suffix, 100);
        // Extension is server-owned, never supplied by the template or substituted values.
        if (!suffix.EndsWith(".cbz", StringComparison.Ordinal)) suffix = $"-{hash}.cbz";
        return TruncateUtf8(stem, 240 - Encoding.UTF8.GetByteCount(suffix)).TrimEnd('.', ' ') + suffix;
    }

    private static string Safe(string value) => Regex.Replace(
        new string(value.Select(c => char.IsControl(c) || "/\\:*?\"<>|".Contains(c) ? '_' : c).ToArray()), @"\s+", " ").Trim();

    private static string TruncateUtf8(string value, int bytes)
    {
        var builder = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (bytes < rune.Utf8SequenceLength) break;
            builder.Append(rune.ToString());
            bytes -= rune.Utf8SequenceLength;
        }
        return builder.ToString();
    }
}

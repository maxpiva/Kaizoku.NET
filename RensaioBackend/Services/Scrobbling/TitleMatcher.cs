using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FuzzySharp;
using FuzzySharp.PreProcess;
using RensaioBackend.Models.Database;

namespace RensaioBackend.Services.Scrobbling;

/// <summary>
/// Title matching engine using fuzzy scoring (FuzzySharp) with media-aware
/// normalization, number/edition penalties, and multi-candidate scoring.
/// </summary>
public class TitleMatcher
{
    /// <summary>
    /// Build all title candidates for a local series (primary title, storage folder,
    /// provider titles). Duplicates are removed (case-insensitive).
    /// </summary>
    public List<string> BuildTitleCandidates(SeriesEntity series)
    {
        var candidates = new List<string>();

        // 1. Primary title
        candidates.Add(series.Title);

        // 2. Storage folder name (often contains the "real" name)
        if (!string.IsNullOrWhiteSpace(series.StoragePath))
            candidates.Add(Path.GetFileName(series.StoragePath.TrimEnd('/', '\\')));

        // 3. Provider titles (each SeriesProvider may have its own title)
        foreach (var source in series.Sources)
        {
            if (!string.IsNullOrWhiteSpace(source.Title) &&
                !candidates.Contains(source.Title, StringComparer.OrdinalIgnoreCase))
                candidates.Add(source.Title);
        }

        return candidates;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Score every candidate (search result) against every original (local) title.
    /// Returns results sorted by descending percentage, then ascending title.
    /// Each result includes the search title, its id, and the best score found
    /// against any original title.
    /// </summary>
    public static (string SearchTitle, TId Id, int Percentage)[] MatchTitles<TId>(
        IEnumerable<string> originalTitles,
        IEnumerable<(string SearchTitle, TId Id)> candidates,
        int minimumScore = 0)
    {
        var originals = originalTitles
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new PreparedTitle(x))
            .ToArray();

        if (originals.Length == 0)
            return Array.Empty<(string SearchTitle, TId Id, int Percentage)>();

        return candidates
            .Where(x => !string.IsNullOrWhiteSpace(x.SearchTitle))
            .Select(candidate =>
            {
                var preparedCandidate = new PreparedTitle(candidate.SearchTitle);

                var bestScore = originals
                    .Select(original => ScoreTitle(original, preparedCandidate))
                    .Max();

                return (
                    candidate.SearchTitle,
                    candidate.Id,
                    Percentage: bestScore
                );
            })
            .Where(x => x.Percentage >= minimumScore)
            .OrderByDescending(x => x.Percentage)
            .ThenBy(x => x.SearchTitle)
            .ToArray();
    }

    // ── Scoring ─────────────────────────────────────────────────────────────

    private static int ScoreTitle(PreparedTitle original, PreparedTitle candidate)
    {
        // Fast path: normalized exact match.
        if (original.Normalized == candidate.Normalized)
            return 100;

        // Strong path: same title after removing weak media/noise words.
        if (original.Core == candidate.Core)
            return 98;

        var weighted = Fuzz.WeightedRatio(original.Normalized, candidate.Normalized, PreprocessMode.Full);
        var tokenSet = Fuzz.TokenSetRatio(original.Core, candidate.Core, PreprocessMode.Full);
        var tokenSort = Fuzz.TokenSortRatio(original.Core, candidate.Core, PreprocessMode.Full);
        var partial = Fuzz.PartialRatio(original.Core, candidate.Core, PreprocessMode.Full);
        var simple = Fuzz.Ratio(original.Core, candidate.Core, PreprocessMode.Full);

        // Good default for manga/comic titles:
        // - TokenSet handles subtitles / extra words.
        // - WeightedRatio is a strong general scorer.
        // - Ratio prevents very loose substring-only matches from winning too much.
        var score =
            0.35 * weighted +
            0.30 * tokenSet +
            0.20 * tokenSort +
            0.10 * simple +
            0.05 * partial;

        score += PrefixBonus(original, candidate);
        score -= ImportantNumberPenalty(original, candidate);
        score -= ExtraImportantTokenPenalty(original, candidate);
        score -= DifferentEditionPenalty(original, candidate);

        return ClampScore((int)Math.Round(score));
    }

    private static int PrefixBonus(PreparedTitle original, PreparedTitle candidate)
    {
        if (original.Core.Length < 4 || candidate.Core.Length < 4)
            return 0;

        if (original.Core.StartsWith(candidate.Core, StringComparison.Ordinal) ||
            candidate.Core.StartsWith(original.Core, StringComparison.Ordinal))
        {
            return 3;
        }

        return 0;
    }

    private static int ImportantNumberPenalty(PreparedTitle original, PreparedTitle candidate)
    {
        if (original.Numbers.Count == 0 || candidate.Numbers.Count == 0)
            return 0;

        if (original.Numbers.SetEquals(candidate.Numbers))
            return 0;

        // Important for things like:
        // "Batman 89" vs "Batman Beyond"
        // "Area 88" vs "Area 51"
        // "20th Century Boys" vs "21st Century Boys"
        return 25;
    }

    private static int ExtraImportantTokenPenalty(PreparedTitle original, PreparedTitle candidate)
    {
        var originalTokens = original.ImportantTokens;
        var candidateTokens = candidate.ImportantTokens;

        if (originalTokens.Count == 0 || candidateTokens.Count == 0)
            return 0;

        var intersection = originalTokens.Intersect(candidateTokens).Count();
        var smaller = Math.Min(originalTokens.Count, candidateTokens.Count);
        var larger = Math.Max(originalTokens.Count, candidateTokens.Count);

        if (intersection == 0)
            return 20;

        // Penalize cases like:
        // "Solo Leveling" vs "Solo Leveling Ragnarok"
        // "Naruto" vs "Boruto Naruto Next Generations"
        var extraRatio = (larger - smaller) / (double)larger;

        if (intersection == smaller && extraRatio >= 0.50)
            return 10;

        if (intersection == smaller && extraRatio >= 0.35)
            return 6;

        return 0;
    }

    private static int DifferentEditionPenalty(PreparedTitle original, PreparedTitle candidate)
    {
        // These are often not the same work, even if the main title matches.
        var dangerousEditionWords = new[]
        {
            "colored", "color", "fullcolor", "full", "deluxe", "omnibus",
            "oneshot", "one-shot", "anthology", "novel", "lightnovel",
            "webnovel", "doujinshi", "spin", "spinoff", "spin-off"
        };

        var originalHas = dangerousEditionWords.Any(w => original.Core.Contains(w));
        var candidateHas = dangerousEditionWords.Any(w => candidate.Core.Contains(w));

        return originalHas != candidateHas ? 5 : 0;
    }

    private static int ClampScore(int value)
    {
        if (value < 0) return 0;
        if (value > 100) return 100;
        return value;
    }

    // ── Title Preparation ───────────────────────────────────────────────────

    private sealed class PreparedTitle
    {
        public string Original { get; }
        public string Normalized { get; }
        public string Core { get; }
        public HashSet<string> ImportantTokens { get; }
        public HashSet<string> Numbers { get; }

        public PreparedTitle(string title)
        {
            Original = title;
            Normalized = Normalize(title);
            Core = RemoveWeakMediaWords(Normalized);
            ImportantTokens = ExtractImportantTokens(Core);
            Numbers = ExtractNumbers(Core);
        }
    }

    /// <summary>
    /// Public, allocation-light title normalization for building/querying token indexes (e.g. the
    /// snapshot search index). Delegates to the same private <see cref="Normalize"/> used by scoring
    /// so index tokens and match tokens agree exactly.
    /// </summary>
    public static string NormalizeForIndex(string title) => Normalize(title);

    private static string Normalize(string value)
    {
        value = RemoveDiacritics(value).ToLowerInvariant();

        // Normalize common separators.
        value = value
            .Replace("&", " and ")
            .Replace("+", " plus ");

        // Remove bracketed release noise, not necessarily all parentheses.
        value = Regex.Replace(value, @"\[(.*?)\]", " ");
        value = Regex.Replace(value, @"\{(.*?)\}", " ");

        // Normalize roman numerals before punctuation stripping.
        value = NormalizeRomanNumerals(value);

        // Remove punctuation/symbols.
        value = Regex.Replace(value, @"[^\p{L}\p{N}]+", " ");

        // Collapse spaces.
        value = Regex.Replace(value, @"\s+", " ").Trim();

        return value;
    }

    private static string RemoveWeakMediaWords(string value)
    {
        // Keep this conservative. Over-removing hurts real title identity.
        var weakWords = new HashSet<string>
        {
            "manga",
            "manhwa",
            "manhua",
            "comic",
            "comics",
            "webtoon",
            "official",
            "english",
            "translation",
            "scan",
            "scans",
            "scanlation",
            "digital"
        };

        var tokens = value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !weakWords.Contains(t));

        return string.Join(' ', tokens);
    }

    private static HashSet<string> ExtractImportantTokens(string value)
    {
        var stopWords = new HashSet<string>
        {
            "the", "a", "an", "and", "or", "of", "to", "in", "on", "for",
            "no", "de", "la", "el"
        };

        return value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1)
            .Where(t => !stopWords.Contains(t))
            .ToHashSet();
    }

    private static HashSet<string> ExtractNumbers(string value)
    {
        return Regex.Matches(value, @"\b\d+\b")
            .Select(m => m.Value.TrimStart('0'))
            .Select(v => v.Length == 0 ? "0" : v)
            .ToHashSet();
    }

    private static string NormalizeRomanNumerals(string value)
    {
        var map = new Dictionary<string, string>
        {
            [" ii "] = " 2 ",
            [" iii "] = " 3 ",
            [" iv "] = " 4 ",
            [" v "] = " 5 ",
            [" vi "] = " 6 ",
            [" vii "] = " 7 ",
            [" viii "] = " 8 ",
            [" ix "] = " 9 ",
            [" x "] = " 10 "
        };

        var padded = " " + value + " ";

        foreach (var pair in map)
            padded = padded.Replace(pair.Key, pair.Value);

        return padded.Trim();
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);

            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

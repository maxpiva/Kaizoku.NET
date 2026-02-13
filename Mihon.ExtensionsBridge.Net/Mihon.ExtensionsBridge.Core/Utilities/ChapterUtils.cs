using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mihon.ExtensionsBridge.Core.Utilities
{
    public static class ChapterUtils
    {
        private const string NUMBER_PATTERN = @"([0-9]+)(\.[0-9]+)?(\.?[a-z]+)?";

        /// <summary>
        /// All cases with Ch.xx
        /// Mokushiroku Alice Vol.1 Ch. 4: Misrepresentation -> 4
        /// </summary>
        private static readonly Regex Basic = new Regex($@"(?<=ch\.) *{NUMBER_PATTERN}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// Example: Bleach 567: Down With Snowwhite -> 567
        /// </summary>
        private static readonly Regex Number = new Regex(NUMBER_PATTERN,
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// Regex used to remove unwanted tags
        /// Example: Prison School 12 v.1 vol004 version1243 volume64 -> Prison School 12
        /// </summary>
        private static readonly Regex Unwanted = new Regex(@"\b(?:v|ver|vol|version|volume|season|s)[^a-z]?[0-9]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// Regex used to remove unwanted whitespace
        /// Example: One Piece 12 special -> One Piece 12special
        /// </summary>
        private static readonly Regex UnwantedWhiteSpace = new Regex(@"\s(?=extra|special|omake)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static decimal ParseChapterNumber(string mangaTitle, string chapterName, float? chapterNumber = null)
        {
            // If chapter number is known return.
            if (chapterNumber is not null && (chapterNumber.Value == -2.0 || chapterNumber.Value > -1.0))
                return Convert.ToDecimal(chapterNumber.Value);

            // Get chapter title with lower case
            var cleanChapterName =
                (chapterName ?? string.Empty)
                    .ToLowerInvariant()
                    // Remove manga title from chapter title.
                    .Replace((mangaTitle ?? string.Empty).ToLowerInvariant(), "")
                    .Trim()
                    // Remove commas or hyphens (normalize to '.')
                    .Replace(',', '.')
                    .Replace('-', '.')
                    // Remove unwanted white spaces.
                    .Replace(UnwantedWhiteSpace, "");

            // Find all number matches
            var numberMatches = Number.Matches(cleanChapterName).Cast<Match>().ToList();

            if (numberMatches.Count == 0)
            {
                return Convert.ToDecimal(chapterNumber ?? -1.0f);
            }

            if (numberMatches.Count > 1)
            {
                // Remove unwanted tags.
                var name = Unwanted.Replace(cleanChapterName, "");

                // Check base case ch.xx
                var basicMatch = Basic.Match(name);
                if (basicMatch.Success)
                    return GetChapterNumberFromMatch(basicMatch);

                // Need to find again; first number might already be removed
                var numberMatch = Number.Match(name);
                if (numberMatch.Success)
                    return GetChapterNumberFromMatch(numberMatch);
            }

            // Return the first number encountered
            return GetChapterNumberFromMatch(numberMatches[0]);
        }

        private static decimal GetChapterNumberFromMatch(Match match)
        {
            // Kotlin code assumes group 1 exists and is parseable.
            var initial = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

            var subChapterDecimal = match.Groups[2].Success ? match.Groups[2].Value : null;
            var subChapterAlpha = match.Groups[3].Success ? match.Groups[3].Value : null;

            var addition = CheckForDecimal(subChapterDecimal, subChapterAlpha);
            return Convert.ToDecimal(initial + addition);
        }

        private static float CheckForDecimal(string? @decimal, string? alpha)
        {
            if (!string.IsNullOrEmpty(@decimal))
            {
                return float.Parse(@decimal, CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrEmpty(alpha))
            {
                // Keep behavior close to Kotlin: "contains" checks
                if (alpha.Contains("extra", StringComparison.OrdinalIgnoreCase))
                    return 0.99f;

                if (alpha.Contains("omake", StringComparison.OrdinalIgnoreCase))
                    return 0.98f;

                if (alpha.Contains("special", StringComparison.OrdinalIgnoreCase))
                    return 0.97f;

                var trimmedAlpha = alpha.TrimStart('.');
                if (trimmedAlpha.Length == 1)
                {
                    return ParseAlphaPostFix(trimmedAlpha[0]);
                }
            }

            return 0.0f;
        }

        /// <summary>
        /// x.a -> x.1, x.b -> x.2, etc (up to i => 0.9; j+ returns 0.0)
        /// </summary>
        private static float ParseAlphaPostFix(char alpha)
        {
            // Kotlin: alpha.code - ('a'.code - 1)
            var number = alpha - ('a' - 1);
            if (number >= 10) return 0.0f;
            return number / 10f;
        }

        /// <summary>
        /// Regex.Replace equivalent to Kotlin's String.replace(Regex, replacement).
        /// </summary>
        private static string Replace(this string input, Regex regex, string replacement) => regex.Replace(input, replacement);

        /// <summary>
        /// Equivalent of Kotlin extension: String.sanitize(title)
        /// </summary>
        public static string Sanitize(string chapterName, string title)
        {
            if (chapterName == null)
                return string.Empty;

            // Step 1: Trim whitespace
            var result = chapterName.Trim();

            // Step 2: Remove prefix if it starts with title
            if (!string.IsNullOrEmpty(title) && result.StartsWith(title, StringComparison.Ordinal))
            {
                result = result.Substring(title.Length);
            }

            // Step 3: Trim unwanted chapter characters
            return result.Trim(CHAPTER_TRIM_CHARS);
        }

        /// <summary>
        /// Characters trimmed from chapter names (whitespace + separators)
        /// </summary>
        private static readonly char[] CHAPTER_TRIM_CHARS =
        {
        // Whitespace
        ' ',
        '\u0009',
        '\u000A',
        '\u000B',
        '\u000C',
        '\u000D',
        '\u0020',
        '\u0085',
        '\u00A0',
        '\u1680',
        '\u2000',
        '\u2001',
        '\u2002',
        '\u2003',
        '\u2004',
        '\u2005',
        '\u2006',
        '\u2007',
        '\u2008',
        '\u2009',
        '\u200A',
        '\u2028',
        '\u2029',
        '\u202F',
        '\u205F',
        '\u3000',

        // Separators
        '-',
        '_',
        ',',
        ':'
    };
    }

}

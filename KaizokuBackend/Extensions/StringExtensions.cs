using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KaizokuBackend.Extensions
{
    /// <summary>
    /// Extension methods for string manipulation and formatting
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Sanitizes a directory path by normalizing directory separators
        /// </summary>
        /// <param name="dir">Directory path to sanitize</param>
        /// <returns>Sanitized directory path</returns>
        public static string SanitizeDirectory(this string dir)
        {
            return dir.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Converts a string into a folder name-safe string by removing invalid characters
        /// </summary>
        /// <param name="input">Input string to sanitize</param>
        /// <returns>Folder name-safe string</returns>
        public static string MakeFolderNameSafe(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return "Unknown";
            }
            var safeName = input.ReplaceInvalidFilenameAndPathCharacters();
            // Ensure the filename isn't empty after cleaning
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "Unknown";
            }

            return safeName;
        }

        /// <summary>
        /// Normalizes a string by removing special characters, converting to lowercase
        /// </summary>
        /// <param name="input">String to normalize</param>
        /// <returns>Normalized string</returns>
        public static string NormalizeGenres(this string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            // Convert to lowercase
            var normalized = input.ToLowerInvariant();

            // Remove special characters and replace with spaces
            normalized = Regex.Replace(normalized, @"[^\w]", " ");

            // Remove extra spaces
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized;
        }

        /// <summary>
        /// Normalizes a title for comparison by removing common words, punctuation, etc.
        /// </summary>
        /// <param name="title">The title to normalize</param>
        /// <returns>Normalized title</returns>
        public static string NormalizeTitle(this string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            // Convert to lowercase
            var normalized = title.ToLowerInvariant();

            // Remove common articles, prepositions, etc.
            var wordsToRemove = new[] { "the ", "a ", "an ", "of ", "in ", "on ", "at ", "by " };
            foreach (var word in wordsToRemove)
            {
                normalized = normalized.Replace(word, " ");
            }

            // Remove common manga/manhwa/manhua suffixes
            var suffixesToRemove = new[] { "season", "chapter", "vol", "volume" };
            foreach (var suffix in suffixesToRemove)
            {
                normalized = Regex.Replace(normalized, $@"\b{suffix}\b", "", RegexOptions.IgnoreCase);
            }

            // Remove punctuation and special characters
            normalized = Regex.Replace(normalized, @"[^\w\s]", "");

            // Remove excess whitespace
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized;
        }

        /// <summary>
        /// Converts a string to Pascal case (first letter of each word capitalized) while preserving original separators and punctuation
        /// </summary>
        /// <param name="input">The input string to convert</param>
        /// <returns>The Pascal case version of the input string with original separators preserved</returns>
        public static string ToPascalCase(this string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var result = new StringBuilder();
            bool capitalizeNext = true;

            for (int i = 0; i < input.Length; i++)
            {
                char currentChar = input[i];

                // Check if current character is a word separator
                if (char.IsWhiteSpace(currentChar) ||
                    currentChar == '-' || currentChar == '_' || currentChar == '.' ||
                    currentChar == '/' || currentChar == '\\' || currentChar == ':' ||
                    currentChar == ';' || currentChar == ',' || currentChar == '!' ||
                    currentChar == '?' || currentChar == '(' || currentChar == ')' ||
                    currentChar == '[' || currentChar == ']' || currentChar == '{' ||
                    currentChar == '}' || currentChar == '"' || currentChar == '\'')
                {
                    // Preserve the separator and mark next letter for capitalization
                    result.Append(currentChar);
                    capitalizeNext = true;
                }
                else if (char.IsLetter(currentChar))
                {
                    // Apply capitalization based on position
                    if (capitalizeNext)
                    {
                        result.Append(char.ToUpperInvariant(currentChar));
                        capitalizeNext = false;
                    }
                    else
                    {
                        result.Append(char.ToLowerInvariant(currentChar));
                    }
                }
                else
                {
                    // Preserve numbers and other characters as-is
                    result.Append(currentChar);
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Determines if two titles are similar enough to be considered the same series
        /// </summary>
        /// <param name="title1">First title</param>
        /// <param name="title2">Second title</param>
        /// <param name="threshold">Similarity threshold (0.0 to 1.0)</param>
        /// <returns>True if titles are similar, false otherwise</returns>
        public static bool AreStringSimilar(this string title1, string title2, double threshold = 0.1)
        {
            if (string.IsNullOrWhiteSpace(title1) || string.IsNullOrWhiteSpace(title2))
            {
                return false;
            }

            // Normalize titles for comparison
            var normalized1 = NormalizeTitle(title1);
            var normalized2 = NormalizeTitle(title2);

            // Calculate similarity using Levenshtein distance
            var distance = normalized1.LevenshteinDistance(normalized2);
            var maxLength = Math.Max(normalized1.Length, normalized2.Length);

            // If strings are similar enough (less than tolerance different), consider them similar
            return maxLength > 0 && (double)distance / maxLength <= threshold;
        }

        /// <summary>
        /// Calculates the Levenshtein distance between two strings
        /// </summary>
        /// <param name="s1">First string</param>
        /// <param name="s2">Second string</param>
        /// <returns>The Levenshtein distance</returns>
        public static int LevenshteinDistance(this string s1, string s2)
        {
            // Handle edge cases
            if (string.IsNullOrEmpty(s1))
            {
                return string.IsNullOrEmpty(s2) ? 0 : s2.Length;
            }

            if (string.IsNullOrEmpty(s2))
            {
                return s1.Length;
            }

            // Create distance matrix
            var matrix = new int[s1.Length + 1, s2.Length + 1];

            // Initialize first row and column
            for (var i = 0; i <= s1.Length; i++)
            {
                matrix[i, 0] = i;
            }

            for (var j = 0; j <= s2.Length; j++)
            {
                matrix[0, j] = j;
            }

            // Fill the matrix
            for (var i = 1; i <= s1.Length; i++)
            {
                for (var j = 1; j <= s2.Length; j++)
                {
                    var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;

                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost
                    );
                }
            }

            // Return the distance
            return matrix[s1.Length, s2.Length];
        }


  
        public static bool IsMatchingProvider(this SeriesProviderEntity sp, ProviderSeriesDetails fs)
        {
            if (fs.Scanlator == fs.Provider || (string.IsNullOrEmpty(fs.Scanlator) && string.IsNullOrEmpty(fs.Provider)))
            {
                return sp.Provider == fs.Provider &&
                       sp.Title == fs.Title &&
                       sp.Language == fs.Lang;
            }
            return sp.Provider == fs.Provider &&
                   sp.Title == fs.Title &&
                   sp.Language == fs.Lang &&
                   sp.Scanlator == fs.Scanlator;
        }

        public static string? RemoveSchemaDomainFromUrl(this string? url)
        {
            if (string.IsNullOrEmpty(url))
                return null;
            if (url.StartsWith("http"))
            {
                int a = url.IndexOf("/api/", StringComparison.CurrentCultureIgnoreCase);
                url = url[(a + 5)..];
            }

            return url;
        }
    }
}

using System.Text.RegularExpressions;

namespace KaizokuBackend.Extensions
{
    /// <summary>
    /// Extension methods for collection operations
    /// </summary>
    public static class CollectionsExtensions
    {
        /// <summary>
        /// Orders collection items using a fair share algorithm
        /// </summary>
        /// <typeparam name="T">Item type</typeparam>
        /// <typeparam name="TSort">Sort key type</typeparam>
        /// <param name="models">Collection to order</param>
        /// <param name="selector">Function to select sort key</param>
        /// <returns>Fair share ordered collection</returns>
        public static List<T> FairShareOrderBy<T, TSort>(this IEnumerable<T> models, Func<T, TSort> selector) where TSort : notnull
        {
            Dictionary<TSort, Queue<T>> grouped = models
                .GroupBy(selector)
                .ToDictionary(a => a.Key, g => new Queue<T>(g));

            List<T> fairshare = [];
            while (grouped.Any(g => g.Value.Count > 0))
            {
                foreach (var key in grouped.Keys.ToList())
                {
                    if (grouped[key].Count > 0)
                    {
                        fairshare.Add(grouped[key].Dequeue());
                    }
                }
            }
            return fairshare;
        }

        /// <summary>
        /// Orders items by chapter number with proper numeric sorting
        /// </summary>
        /// <typeparam name="T">Item type</typeparam>
        /// <param name="items">Collection to order</param>
        /// <param name="selector">Function to select the chapter string</param>
        /// <returns>Ordered collection</returns>
        public static IEnumerable<T> OrderByChapter<T>(this IEnumerable<T> items, Func<T, string> selector)
        {
            var digitChecks = new Regex(@"\d+(\.\d+)?", RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
            var list = items.ToList();

            // Determine the max length of the last numeric chunk
            var maxDigits = list
                .Select(i =>
                {
                    var matches = digitChecks.Matches(selector(i));
                    return matches.Count > 0 ? (int?)matches[^1].Value.Length : null;
                })
                .Max() ?? 0;

            return list.OrderBy(i =>
            {
                var input = selector(i);
                var matches = digitChecks.Matches(input);

                if (matches.Count == 0)
                    return input;

                var lastMatch = matches[^1];
                var padded = lastMatch.Value.PadLeft(maxDigits, '0');

                // Reconstruct sort key: original string, with only last match replaced (by position)
                var result = input[..lastMatch.Index] + padded +
                             input[(lastMatch.Index + lastMatch.Length)..];
                return result;
            }, StringComparer.InvariantCulture);
        }

        /// <summary>
        /// Orders items by their Levenshtein distance to a comparison string
        /// </summary>
        /// <typeparam name="T">Item type</typeparam>
        /// <param name="items">Collection to order</param>
        /// <param name="expression">Function to extract string for comparison</param>
        /// <param name="compareTo">String to compare against</param>
        /// <returns>Ordered collection</returns>
        public static IEnumerable<T> OrderByLevenshteinDistance<T>(this IEnumerable<T> items, Func<T, string> expression, string compareTo)
        {
            return items.OrderBy(s => expression(s).LevenshteinDistance(compareTo));
        }

        /// <summary>
        /// Converts a collection of strings to a distinct collection of Pascal case strings
        /// </summary>
        /// <param name="strings">The collection of strings to convert</param>
        /// <returns>A distinct collection of Pascal case strings</returns>
        public static List<string> ToDistinctPascalCase(this IEnumerable<string> strings)
        {
            return strings
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.ToPascalCase())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
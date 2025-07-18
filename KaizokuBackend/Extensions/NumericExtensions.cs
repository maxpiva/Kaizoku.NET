using System.Globalization;

namespace KaizokuBackend.Extensions
{
    /// <summary>
    /// Extension methods for numeric operations and formatting
    /// </summary>
    public static class NumericExtensions
    {
        /// <summary>
        /// Converts a collection of nullable decimals to ranges
        /// </summary>
        /// <param name="values">Collection of nullable decimal values</param>
        /// <param name="tolerance">Tolerance for combining adjacent values</param>
        /// <returns>List of ranges (From, To)</returns>
        public static List<(decimal From, decimal To)> DecimalRanges(this IEnumerable<decimal?> values, decimal tolerance = 1.1m)
        {
            IEnumerable<decimal> dec = values.Where(a => a.HasValue).Select(a => a!.Value).ToList();
            return DecimalRanges(dec, tolerance);
        }

        /// <summary>
        /// Converts a collection of decimals to ranges
        /// </summary>
        /// <param name="values">Collection of decimal values</param>
        /// <param name="tolerance">Tolerance for combining adjacent values</param>
        /// <returns>List of ranges (From, To)</returns>
        public static List<(decimal From, decimal To)> DecimalRanges(this IEnumerable<decimal> values, decimal tolerance = 1.1m)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var ranges = new List<(decimal From, decimal To)>();

            if (sorted.Count == 0) return ranges;

            decimal rangeStart = sorted[0];
            decimal rangeEnd = sorted[0];

            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] - rangeEnd <= tolerance)
                {
                    rangeEnd = sorted[i];
                }
                else
                {
                    ranges.Add((rangeStart, rangeEnd));
                    rangeStart = rangeEnd = sorted[i];
                }
            }

            ranges.Add((rangeStart, rangeEnd));
            return ranges;
        }

        /// <summary>
        /// Formats a decimal value for display
        /// </summary>
        /// <param name="value">Decimal value to format</param>
        /// <returns>Formatted decimal string</returns>
        public static string FormatDecimal(this decimal value)
        {
            return (value % 1 == 0) ? ((int)value).ToString() : value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Formats a range tuple for display
        /// </summary>
        /// <param name="r">Range tuple (From, To)</param>
        /// <returns>Formatted range string</returns>
        public static string FormatStartStop((decimal From, decimal To) r)
        {
            return r.From != r.To ? $"{r.From.FormatDecimal()}-{r.To.FormatDecimal()}" : r.From.FormatDecimal();
        }

        /// <summary>
        /// Formats a collection of decimals as ranges
        /// </summary>
        /// <param name="values">Collection of decimal values</param>
        /// <param name="tolerance">Tolerance for combining adjacent values</param>
        /// <returns>Formatted ranges string</returns>
        public static string FormatDecimalRanges(this IEnumerable<decimal> values, decimal tolerance = 1.1m)
        {
            return string.Join(",", DecimalRanges(values, tolerance).Select(r => FormatStartStop(r)));
        }

        /// <summary>
        /// Formats a collection of nullable decimals as ranges
        /// </summary>
        /// <param name="values">Collection of nullable decimal values</param>
        /// <param name="tolerance">Tolerance for combining adjacent values</param>
        /// <returns>Formatted ranges string</returns>
        public static string FormatDecimalRanges(this IEnumerable<decimal?> values, decimal tolerance = 1.1m)
        {
            IEnumerable<decimal> dec = values.Where(a => a.HasValue).Select(a => a!.Value).ToList();
            return string.Join(",", DecimalRanges(dec, tolerance).Select(r => FormatStartStop(r)));
        }

        /// <summary>
        /// Gets the maximum value from a collection of nullable decimals
        /// </summary>
        /// <param name="dec">Collection of nullable decimals</param>
        /// <returns>Maximum value or null if collection is empty</returns>
        public static decimal? MaxNull(this IEnumerable<decimal?> dec)
        {
            if (dec.Any(a => a.HasValue))
                return dec.Where(a => a.HasValue).Max(a => a!.Value);
            return null;
        }

        /// <summary>
        /// Gets the maximum value from a collection using a selector function
        /// </summary>
        /// <typeparam name="S">Source type</typeparam>
        /// <typeparam name="T">Target type</typeparam>
        /// <param name="dec">Collection of source items</param>
        /// <param name="func">Selector function</param>
        /// <returns>Maximum value or null if collection is empty</returns>
        public static T? MaxNull<S, T>(this IEnumerable<S> dec, Func<S, T?> func) where T : struct
        {
            List<T?> values = dec.Select(func).ToList();
            if (values.Any(a => a.HasValue))
            {
                return values.Where(a => a.HasValue).Max(a => a);
            }
            return null;
        }

        /// <summary>
        /// Selects an integer based on specific criteria from a collection
        /// </summary>
        /// <param name="integers">Collection of integers to analyze</param>
        /// <param name="maximum">The maximum value to consider</param>
        /// <returns>Selected integer based on the criteria</returns>
        public static int LeastUsedInteger(this IEnumerable<int> integers, int maximum)
        {
            // Convert to list for multiple enumeration
            var intList = integers.Where(i => i >= 0 && i < maximum).ToList();

            // Check if all integers from 0 to maximum-1 exist
            var allExist = true;
            var missing = new List<int>();

            for (int i = 0; i < maximum; i++)
            {
                if (!intList.Contains(i))
                {
                    allExist = false;
                    missing.Add(i);
                }
            }

            if (allExist)
            {
                // All integers exist, find least repeated ones
                var countByNumber = intList.GroupBy(i => i)
                    .ToDictionary(g => g.Key, g => g.Count());

                var minCount = countByNumber.Values.Min();
                var leastRepeated = countByNumber
                    .Where(kvp => kvp.Value == minCount)
                    .Select(kvp => kvp.Key)
                    .ToList();

                // Return a random one from the least repeated integers
                return leastRepeated[new Random().Next(leastRepeated.Count)];
            }
            else
            {
                // Some integers are missing, find the one with the largest gap
                var sortedList = intList.Distinct().OrderBy(i => i).ToList();
                int maxGap = 0;
                int selectedMissing = missing[0];

                foreach (var m in missing)
                {
                    // Find previous and next integers
                    int prev = sortedList.Where(i => i < m).DefaultIfEmpty(-1).Max();
                    int next = sortedList.Where(i => i > m).DefaultIfEmpty(maximum).Min();

                    // Calculate gap
                    int gap = next - prev - 1;

                    if (gap > maxGap)
                    {
                        maxGap = gap;
                        selectedMissing = m;
                    }
                }

                return selectedMissing;
            }
        }
    }
}
using System.Text.RegularExpressions;

namespace WordParserCore
{
    public static class StringExtensions
    {
        private static readonly Regex WhitespaceCollapse = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex OrdinalCapture = new(@"^([^\)]+)\)", RegexOptions.Compiled);
        private static readonly Regex DateCapture = new(@"(\d{1,2})\s+(\w+)\s+(\d{4})", RegexOptions.Compiled);

        public static string Sanitize(this string input)
        {
            input = WhitespaceCollapse.Replace(input, " ");
            return input.Replace("\u2013", "-");
        }

        public static string ExtractOrdinal(this string input)
        {
            var match = OrdinalCapture.Match(input);
            return match.Success ? match.Groups[1].Value : "Unknown";
        }

        private static readonly Dictionary<string, int> MonthNames = new Dictionary<string, int>
        {
            {"stycznia", 1}, {"lutego", 2}, {"marca", 3}, {"kwietnia", 4},
            {"maja", 5}, {"czerwca", 6}, {"lipca", 7}, {"sierpnia", 8},
            {"września", 9}, {"października", 10}, {"listopada", 11}, {"grudnia", 12}
        };

        public static DateTime ExtractDate(this string input)
        {
            //sample input: "z dnia 29 maja 2020 r."
            var match = DateCapture.Match(input);

            if (!match.Success)
            {
                // throw new ArgumentException("Nie udało się sparsować daty z ciągu wejściowego", nameof(input));
                return DateTime.MinValue; // Zwracamy minimalną datę, jeśli parsowanie się nie powiodło
            }

            var day = int.Parse(match.Groups[1].Value);
            var monthName = match.Groups[2].Value.ToLower();
            var year = int.Parse(match.Groups[3].Value);

            if (!MonthNames.TryGetValue(monthName, out int month))
            {
                throw new ArgumentException($"Nieprawidłowa nazwa miesiąca: {monthName}", nameof(input));
            }

            return new DateTime(year, month, day);
        }
    }
}
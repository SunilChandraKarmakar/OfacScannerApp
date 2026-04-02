using System.Text.RegularExpressions;

namespace OfacScannerApp.Utilities
{
    public static class StringMatcher
    {
        public static int CalculateScore(string source, string target)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
                return 0;

            var s = Regex.Replace(source.ToLowerInvariant(), @"\s+", "");
            var t = Regex.Replace(target.ToLowerInvariant(), @"\s+", "");

            if (s == t)
                return 100;

            if (s.Length < 3 || t.Length < 3)
                return s == t ? 100 : 0;

            int distance = LevenshteinDistance(s, t);
            int maxLen = Math.Max(s.Length, t.Length);

            if (maxLen == 0)
                return 100;

            double similarity = 1.0 - ((double)distance / maxLen);
            int score = (int)Math.Round(similarity * 100);

            if (score < 0) score = 0;
            if (score > 100) score = 100;

            return score;
        }

        public static int TokenOverlapScore(string source, string target)
        {
            var sTokens = SplitTokens(source);
            var tTokens = SplitTokens(target);

            if (sTokens.Count == 0 || tTokens.Count == 0)
                return 0;

            int common = sTokens.Intersect(tTokens, StringComparer.OrdinalIgnoreCase).Count();
            int total = Math.Max(sTokens.Count, tTokens.Count);

            return (int)Math.Round((double)common / total * 100);
        }

        private static HashSet<string> SplitTokens(string value)
        {
            return value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static int LevenshteinDistance(string s, string t)
        {
            var dp = new int[s.Length + 1, t.Length + 1];

            for (int i = 0; i <= s.Length; i++) dp[i, 0] = i;
            for (int j = 0; j <= t.Length; j++) dp[0, j] = j;

            for (int i = 1; i <= s.Length; i++)
            {
                for (int j = 1; j <= t.Length; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;

                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost
                    );
                }
            }

            return dp[s.Length, t.Length];
        }
    }
}
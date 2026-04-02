namespace OfacScannerApp.Models
{
    public class MatchResult
    {
        public bool IsMatch { get; set; }
        public int Score { get; set; }
        public string InputName { get; set; } = string.Empty;

        public string MatchedName { get; set; } = string.Empty;
        public string MatchedAlias { get; set; } = string.Empty;

        public string MatchType { get; set; } = "NONE"; // EXACT / TOKEN / FUZZY / NONE
        public string Remarks { get; set; } = string.Empty;
    }
}
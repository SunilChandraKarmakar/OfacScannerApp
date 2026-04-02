namespace OfacScannerApp.Models
{
    public class OfacIndex
    {
        // normalized name -> entity
        public Dictionary<string, OfacEntity> ExactNameMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // token -> set of normalized names
        public Dictionary<string, HashSet<string>> TokenIndex { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // normalized name -> original display name
        public Dictionary<string, string> DisplayNameMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> AllNormalizedNames { get; set; } = new();
    }
}
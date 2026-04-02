namespace OfacScannerApp.Models
{
    public class OfacEntity
    {
        public string PrimaryName { get; set; } = string.Empty;
        public List<string> Aliases { get; set; } = new();
    }
}
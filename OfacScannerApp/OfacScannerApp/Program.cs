using OfacScannerApp.Services;
using System.Diagnostics;

var service = new OfacService();

// Load index ONLY ONCE (very important for performance)
Console.WriteLine("Loading OFAC data... Please wait...\n");
var index = await service.LoadIndexAsync();

Console.WriteLine("=== OFAC Sanction Scanner ===");
Console.WriteLine("Type 'exit' to close the program.\n");

while (true)
{
    Console.Write("Enter Customer Name: ");
    var inputName = Console.ReadLine();

    // Manual exit condition
    if (string.Equals(inputName, "exit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("\nClosing application... Goodbye!");
        break;
    }

    if (string.IsNullOrWhiteSpace(inputName))
    {
        Console.WriteLine("Invalid input! Try again.\n");
        continue;
    }

    var sw = Stopwatch.StartNew();

    var result = service.FindBestMatch(inputName, index, alertThreshold: 70);

    sw.Stop();

    Console.WriteLine("\n===== RESULT =====");

    if (result.IsMatch)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("⚠️ ALERT: Possible OFAC match found.");
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✅ SAFE: No strong OFAC match found.");
    }

    Console.ResetColor();

    Console.WriteLine($"Input Name     : {result.InputName}");
    Console.WriteLine($"Match Status   : {(result.IsMatch ? "MATCH" : "NO MATCH")}");
    Console.WriteLine($"Score          : {result.Score}/100");
    Console.WriteLine($"Match Type     : {result.MatchType}");
    Console.WriteLine($"Matched Name   : {result.MatchedName}");
    Console.WriteLine($"Matched Alias  : {result.MatchedAlias}");
    Console.WriteLine($"Remarks        : {result.Remarks}");
    Console.WriteLine($"Completed In   : {sw.ElapsedMilliseconds} ms");

    Console.WriteLine("\n==================\n");
}
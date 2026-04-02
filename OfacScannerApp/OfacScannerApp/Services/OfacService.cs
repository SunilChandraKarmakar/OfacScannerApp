using OfacScannerApp.Models;
using OfacScannerApp.Utilities;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OfacScannerApp.Services
{
    public class OfacService
    {
        private const string OfacUrl = "https://www.treasury.gov/ofac/downloads/sdn.xml";

        private static readonly XNamespace NS =
            "https://sanctionslistservice.ofac.treas.gov/api/PublicationPreview/exports/XML";

        private static readonly HttpClient HttpClient = CreateHttpClient();

        private static readonly string CacheFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OfacScannerApp");

        private static readonly string CacheFilePath = Path.Combine(CacheFolder, "ofac-entities-cache.json");

        private static readonly SemaphoreSlim CacheLock = new(1, 1);

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
            client.DefaultRequestHeaders.Add("Accept", "application/xml");
            client.Timeout = TimeSpan.FromSeconds(60);
            return client;
        }

        public async Task<OfacIndex> LoadIndexAsync()
        {
            Directory.CreateDirectory(CacheFolder);

            await CacheLock.WaitAsync();
            try
            {
                var entities = await TryLoadCachedEntitiesAsync();

                if (entities == null || entities.Count == 0)
                {
                    entities = await DownloadEntitiesAsync();
                    await SaveCacheAsync(entities);
                }

                return BuildIndex(entities);
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public MatchResult FindBestMatch(string input, OfacIndex index, int alertThreshold = 70)
        {
            var inputNormalized = NormalizeFull(input);

            if (string.IsNullOrWhiteSpace(inputNormalized))
            {
                return new MatchResult
                {
                    InputName = input,
                    IsMatch = false,
                    Score = 0,
                    MatchType = "NONE",
                    Remarks = "Input is empty."
                };
            }

            // Exact match
            if (index.ExactNameMap.TryGetValue(inputNormalized, out var exactEntity))
            {
                return new MatchResult
                {
                    InputName = input,
                    IsMatch = true,
                    Score = 100,
                    MatchType = "EXACT",
                    MatchedName = exactEntity.PrimaryName,
                    MatchedAlias = exactEntity.PrimaryName,
                    Remarks = "Exact name matched with OFAC record."
                };
            }

            var inputTokens = SplitTokens(inputNormalized);

            // Candidate reduction by token index
            HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);

            foreach (var token in inputTokens)
            {
                if (index.TokenIndex.TryGetValue(token, out var matchedNames))
                    candidates.UnionWith(matchedNames);
            }

            if (candidates.Count == 0)
                candidates = index.AllNormalizedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

            MatchResult? best = null;

            foreach (var normalizedCandidate in candidates)
            {
                var displayCandidate = index.DisplayNameMap.TryGetValue(normalizedCandidate, out var dn)
                    ? dn
                    : normalizedCandidate;

                int fuzzyScore = StringMatcher.CalculateScore(inputNormalized, normalizedCandidate);
                int tokenScore = StringMatcher.TokenOverlapScore(inputNormalized, normalizedCandidate);

                int finalScore = Math.Max(fuzzyScore, tokenScore);
                string matchType = finalScore == tokenScore ? "TOKEN" : "FUZZY";

                if (inputTokens.Length > 0)
                {
                    var candidateTokens = SplitTokens(normalizedCandidate);
                    if (inputTokens.All(t => candidateTokens.Contains(t)))
                    {
                        finalScore = Math.Max(finalScore, 90);
                        matchType = "TOKEN";
                    }
                }

                var entity = index.ExactNameMap[normalizedCandidate];

                var result = new MatchResult
                {
                    InputName = input,
                    IsMatch = finalScore >= alertThreshold,
                    Score = finalScore,
                    MatchType = matchType,
                    MatchedName = entity.PrimaryName,
                    MatchedAlias = displayCandidate,
                    Remarks = finalScore >= alertThreshold
                        ? "Potential sanction match found."
                        : "Below alert threshold."
                };

                if (best == null || result.Score > best.Score)
                    best = result;
            }

            return best ?? new MatchResult
            {
                InputName = input,
                IsMatch = false,
                Score = 0,
                MatchType = "NONE",
                Remarks = "No matching candidates found."
            };
        }

        private async Task<List<OfacEntity>> DownloadEntitiesAsync()
        {
            using var response = await HttpClient.GetAsync(OfacUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();

            var settings = new XmlReaderSettings
            {
                Async = true,
                IgnoreWhitespace = true,
                IgnoreComments = true,
                DtdProcessing = DtdProcessing.Prohibit
            };

            using var reader = XmlReader.Create(stream, settings);

            var entities = new List<OfacEntity>();

            while (await reader.ReadAsync())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sdnEntry")
                {
                    var entry = (XElement)XNode.ReadFrom(reader);
                    var entity = ExtractEntity(entry);

                    if (!string.IsNullOrWhiteSpace(entity.PrimaryName))
                        entities.Add(entity);
                }
            }

            return entities;
        }

        private OfacEntity ExtractEntity(XElement x)
        {
            var entity = new OfacEntity();

            var lastName = x.Element(NS + "lastName")?.Value?.Trim() ?? "";
            var firstName = x.Element(NS + "firstName")?.Value?.Trim() ?? "";

            var primaryName = $"{firstName} {lastName}".Trim();

            if (string.IsNullOrWhiteSpace(primaryName))
                primaryName = lastName.Trim();

            entity.PrimaryName = primaryName;

            var aliases = new List<string>();

            if (lastName.Contains(","))
            {
                var parts = lastName.Split(',');
                if (parts.Length >= 2)
                    aliases.Add($"{parts[1].Trim()} {parts[0].Trim()}");
            }

            aliases.AddRange(
                x.Descendants(NS + "aka")
                 .Select(a =>
                 {
                     var fn = a.Element(NS + "firstName")?.Value?.Trim() ?? "";
                     var ln = a.Element(NS + "lastName")?.Value?.Trim() ?? "";
                     return $"{fn} {ln}".Trim();
                 })
                 .Where(n => !string.IsNullOrWhiteSpace(n))
            );

            entity.Aliases = aliases
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return entity;
        }

        private OfacIndex BuildIndex(List<OfacEntity> entities)
        {
            var index = new OfacIndex();

            foreach (var entity in entities)
            {
                var allNames = new List<string> { entity.PrimaryName };
                allNames.AddRange(entity.Aliases);

                foreach (var rawName in allNames.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    var normalized = NormalizeFull(rawName);

                    if (string.IsNullOrWhiteSpace(normalized))
                        continue;

                    if (!index.ExactNameMap.ContainsKey(normalized))
                        index.ExactNameMap[normalized] = entity;

                    if (!index.DisplayNameMap.ContainsKey(normalized))
                        index.DisplayNameMap[normalized] = rawName;

                    index.AllNormalizedNames.Add(normalized);

                    foreach (var token in SplitTokens(normalized))
                    {
                        if (!index.TokenIndex.TryGetValue(token, out var bucket))
                        {
                            bucket = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            index.TokenIndex[token] = bucket;
                        }

                        bucket.Add(normalized);
                    }
                }
            }

            index.AllNormalizedNames = index.AllNormalizedNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return index;
        }

        private async Task<List<OfacEntity>?> TryLoadCachedEntitiesAsync()
        {
            if (!File.Exists(CacheFilePath))
                return null;

            var json = await File.ReadAllTextAsync(CacheFilePath);
            var cache = JsonSerializer.Deserialize<OfacCache>(json);

            if (cache == null || cache.Entities == null || cache.Entities.Count == 0)
                return null;

            if (DateTime.UtcNow - cache.FetchedAtUtc > TimeSpan.FromHours(24))
                return null;

            return cache.Entities;
        }

        private async Task SaveCacheAsync(List<OfacEntity> entities)
        {
            var cache = new OfacCache
            {
                FetchedAtUtc = DateTime.UtcNow,
                Entities = entities
            };

            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(CacheFilePath, json);
        }

        private static string NormalizeFull(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var normalized = name.ToLowerInvariant();
            normalized = Regex.Replace(normalized, @"[^\w\s]", "");
            normalized = Regex.Replace(normalized, @"\s+", " ");
            return normalized.Trim();
        }

        private static string[] SplitTokens(string value)
        {
            return value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private class OfacCache
        {
            public DateTime FetchedAtUtc { get; set; }
            public List<OfacEntity> Entities { get; set; } = new();
        }
    }
}
using System.Net.Http.Headers;
using System.Text.Json;

namespace WinVelo.Tools.SourceChecker;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("  WinVelo Catalog Source Checker (CI)  ");
        Console.WriteLine("========================================");

        string root = AppDomain.CurrentDomain.BaseDirectory;
        string tweaksDir = Path.Combine(root, "Catalog", "Tweaks");
        if (!Directory.Exists(tweaksDir))
        {
            string? cur = Directory.GetCurrentDirectory();
            while (cur != null)
            {
                string candidate = Path.Combine(cur, "Catalog", "Tweaks");
                if (Directory.Exists(candidate))
                {
                    tweaksDir = candidate;
                    break;
                }
                cur = Directory.GetParent(cur)?.FullName;
            }
        }

        if (!Directory.Exists(tweaksDir))
        {
            Console.WriteLine($"[ERROR] Cannot find Catalog/Tweaks directory from '{root}'.");
            return 1;
        }

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        using var client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        var jsonFiles = Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories);
        Console.WriteLine($"[INFO] Found {jsonFiles.Length} catalog file(s) in {tweaksDir}\n");

        int totalChecked = 0;
        int failedCount = 0;
        var failureReports = new List<(string Id, string Url, int StatusCode, string Reason)>();

        foreach (var file in jsonFiles)
        {
            string fileName = Path.GetFileName(file);
            Console.WriteLine($"[SCANNING] {fileName}...");
            string json = await File.ReadAllTextAsync(file);
            using var doc = JsonDocument.Parse(json);

            foreach (var tweak in doc.RootElement.EnumerateArray())
            {
                totalChecked++;
                string id = tweak.GetProperty("id").GetString() ?? "unknown";
                string name = tweak.GetProperty("name").GetString() ?? "";

                // Get source URL(s)
                var urls = new List<string>();
                var sourceElement = tweak.GetProperty("source");
                if (sourceElement.ValueKind == JsonValueKind.String)
                {
                    string? s = sourceElement.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) urls.Add(s);
                }
                else if (sourceElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in sourceElement.EnumerateArray())
                    {
                        string? s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) urls.Add(s);
                    }
                }

                if (urls.Count == 0)
                {
                    Console.WriteLine($"  [FAIL] {id}: Missing source URL");
                    failureReports.Add((id, "None", 0, "No source URL provided"));
                    failedCount++;
                    continue;
                }

                // Collect search keywords (valueName, policyName, key segments, URL anchors, feature words)
                var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (tweak.TryGetProperty("apply", out var applyArr) && applyArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var action in applyArr.EnumerateArray())
                    {
                        if (action.TryGetProperty("valueName", out var vn) && !string.IsNullOrWhiteSpace(vn.GetString()))
                        {
                            string val = vn.GetString()!;
                            if (!val.Equals("(Default)", StringComparison.OrdinalIgnoreCase) &&
                                !val.Equals("Value", StringComparison.OrdinalIgnoreCase) &&
                                !val.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                            {
                                keywords.Add(val);
                            }
                        }
                        if (action.TryGetProperty("key", out var keyProp) && !string.IsNullOrWhiteSpace(keyProp.GetString()))
                        {
                            string keyStr = keyProp.GetString()!;
                            var segments = keyStr.Split('\\', '/');
                            foreach (var seg in segments)
                            {
                                if (seg.Length >= 4 &&
                                    !seg.Equals("Software", StringComparison.OrdinalIgnoreCase) &&
                                    !seg.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) &&
                                    !seg.Equals("Windows", StringComparison.OrdinalIgnoreCase) &&
                                    !seg.Equals("CurrentVersion", StringComparison.OrdinalIgnoreCase) &&
                                    !seg.Equals("Policies", StringComparison.OrdinalIgnoreCase) &&
                                    !seg.Equals("System", StringComparison.OrdinalIgnoreCase) &&
                                    !seg.Equals("Control", StringComparison.OrdinalIgnoreCase))
                                {
                                    keywords.Add(seg);
                                }
                            }
                        }
                        if (action.TryGetProperty("serviceName", out var sn) && !string.IsNullOrWhiteSpace(sn.GetString()))
                        {
                            keywords.Add(sn.GetString()!);
                        }
                        if (action.TryGetProperty("taskName", out var tn) && !string.IsNullOrWhiteSpace(tn.GetString()))
                        {
                            keywords.Add(tn.GetString()!);
                        }
                    }
                }

                // ID parts (feature tokens)
                var idParts = id.Split('.');
                foreach (var part in idParts)
                {
                    var subparts = part.Split('-');
                    foreach (var sp in subparts)
                    {
                        if (sp.Length >= 4 &&
                            !sp.Equals("disable", StringComparison.OrdinalIgnoreCase) &&
                            !sp.Equals("enable", StringComparison.OrdinalIgnoreCase) &&
                            !sp.Equals("system", StringComparison.OrdinalIgnoreCase))
                        {
                            keywords.Add(sp);
                        }
                    }
                }

                // Feature words from tweak name
                var nameWords = name.Split(new[] { ' ', '-', '/', '(', ')', '[', ']', '&', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var nw in nameWords)
                {
                    if (nw.Length >= 5 &&
                        !nw.Equals("Windows", StringComparison.OrdinalIgnoreCase) &&
                        !nw.Equals("Disable", StringComparison.OrdinalIgnoreCase) &&
                        !nw.Equals("Enable", StringComparison.OrdinalIgnoreCase) &&
                        !nw.Equals("System", StringComparison.OrdinalIgnoreCase) &&
                        !nw.Equals("Prevent", StringComparison.OrdinalIgnoreCase) &&
                        !nw.Equals("Ensure", StringComparison.OrdinalIgnoreCase))
                    {
                        keywords.Add(nw);
                    }
                }

                bool anyUrlOk = false;
                string lastFailReason = "";
                int lastStatusCode = 0;
                string checkedUrl = urls[0];

                foreach (var url in urls)
                {
                    checkedUrl = url;

                    // Add URL anchor tokens if present
                    int anchorIdx = url.IndexOf('#');
                    if (anchorIdx >= 0 && anchorIdx < url.Length - 1)
                    {
                        string anchor = url[(anchorIdx + 1)..];
                        var anchorTokens = anchor.Split('-', '_');
                        foreach (var at in anchorTokens)
                        {
                            if (at.Length >= 4) keywords.Add(at);
                        }
                    }

                    try
                    {
                        using var resp = await client.GetAsync(url);
                        lastStatusCode = (int)resp.StatusCode;

                        if (!resp.IsSuccessStatusCode)
                        {
                            lastFailReason = $"HTTP status {(int)resp.StatusCode} ({resp.ReasonPhrase})";
                            continue;
                        }

                        string html = await resp.Content.ReadAsStringAsync();

                        // Check keywords against both page content and URL structure
                        bool keywordMatched = keywords.Count == 0;
                        foreach (var kw in keywords)
                        {
                            if (html.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                                url.Contains(kw, StringComparison.OrdinalIgnoreCase))
                            {
                                keywordMatched = true;
                                break;
                            }
                        }

                        if (!keywordMatched)
                        {
                            lastFailReason = $"HTTP 200 OK, but page does not mention target identifiers: [{string.Join(", ", keywords)}]";
                            continue;
                        }

                        anyUrlOk = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastStatusCode = 0;
                        lastFailReason = $"Request exception: {ex.Message}";
                    }
                }

                if (anyUrlOk)
                {
                    Console.WriteLine($"  [PASS] {id} -> 200 OK");
                }
                else
                {
                    Console.WriteLine($"  [FAIL] {id} -> {lastFailReason} ({checkedUrl})");
                    failureReports.Add((id, checkedUrl, lastStatusCode, lastFailReason));
                    failedCount++;
                }
            }
        }

        Console.WriteLine("\n----------------------------------------");
        Console.WriteLine($"Summary: {totalChecked} tweak(s) checked. {failedCount} failure(s).");
        Console.WriteLine("----------------------------------------\n");

        if (failureReports.Count > 0)
        {
            Console.WriteLine("Failure Details:");
            foreach (var f in failureReports)
            {
                Console.WriteLine($"- [{f.Id}] (Status {f.StatusCode}): {f.Reason}");
                Console.WriteLine($"  URL: {f.Url}");
            }
            return 1;
        }

        return 0;
    }
}

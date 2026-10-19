using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace WinVelo.Tests.CatalogTests;

public sealed class ValidatorRulesTests
{
    private static (string Signature, string Sha256) ComputeApplySignature(JsonNode item)
    {
        var applyArray = item["apply"]?.AsArray();
        if (applyArray == null || applyArray.Count == 0) return (string.Empty, string.Empty);

        var targets = new List<string>();
        foreach (var action in applyArray)
        {
            if (action == null) continue;
            string? hive = action["hive"]?.ToString();
            string? key = action["key"]?.ToString();
            string? val = action["valueName"]?.ToString();
            string? svc = action["serviceName"]?.ToString();

            if (!string.IsNullOrWhiteSpace(hive) || !string.IsNullOrWhiteSpace(key))
            {
                string h = (hive ?? "").ToUpperInvariant().Replace("HKEY_LOCAL_MACHINE", "HKLM").Replace("HKEY_CURRENT_USER", "HKCU");
                string k = (key ?? "").Trim('\\').ToUpperInvariant();
                string v = string.IsNullOrWhiteSpace(val) ? "@DEFAULT" : val.ToUpperInvariant();
                string data = action["data"]?.ToString() ?? "";
                targets.Add($"REG:{h}\\{k}\\{v}={data}");
            }
            else if (!string.IsNullOrWhiteSpace(svc))
            {
                string s = svc.ToUpperInvariant();
                string startup = action["startupType"]?.ToString() ?? "";
                targets.Add($"SVC:{s}={startup}");
            }
            else
            {
                string? type = action["type"]?.ToString();
                string? script = action["script"]?.ToString();
                if (string.Equals(type, "PowerShell", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(script))
                {
                    targets.Add($"PS:{script.Trim()}");
                }
            }
        }

        targets.Sort(StringComparer.Ordinal);
        string signature = string.Join(";", targets);
        if (string.IsNullOrWhiteSpace(signature)) return (string.Empty, string.Empty);

        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(signature));
        string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return (signature, hash);
    }

    private static bool HasOppositeMarking(JsonNode tweakA, string idA, JsonNode tweakB, string idB)
    {
        bool Matches(JsonNode t, string targetId)
        {
            var opp = t["oppositeOf"];
            if (opp is JsonValue val && val.TryGetValue<string>(out var s))
            {
                return string.Equals(s, targetId, StringComparison.OrdinalIgnoreCase);
            }
            if (opp is JsonArray arr)
            {
                return arr.Any(n => string.Equals(n?.ToString(), targetId, StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        return Matches(tweakA, idB) || Matches(tweakB, idA);
    }

    private static List<string> CheckConflictingRegistryData(Dictionary<string, JsonNode> tweakMap)
    {
        var errors = new List<string>();
        var regValues = new Dictionary<string, List<(string TweakId, string Data, JsonNode TweakNode)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, item) in tweakMap)
        {
            var applyArray = item["apply"]?.AsArray();
            if (applyArray == null || applyArray.Count == 0) continue;

            foreach (var action in applyArray)
            {
                if (action == null) continue;
                string? hive = action["hive"]?.ToString();
                string? key = action["key"]?.ToString();
                string? val = action["valueName"]?.ToString();

                if (!string.IsNullOrWhiteSpace(hive) || !string.IsNullOrWhiteSpace(key))
                {
                    string h = (hive ?? "").ToUpperInvariant().Replace("HKEY_LOCAL_MACHINE", "HKLM").Replace("HKEY_CURRENT_USER", "HKCU");
                    string k = (key ?? "").Trim('\\').ToUpperInvariant();
                    string v = string.IsNullOrWhiteSpace(val) ? "@DEFAULT" : val.ToUpperInvariant();
                    string data = action["data"]?.ToString() ?? "";
                    string target = $"REG:{h}\\{k}\\{v}";

                    if (!regValues.TryGetValue(target, out var list))
                    {
                        list = new List<(string, string, JsonNode)>();
                        regValues[target] = list;
                    }

                    if (!list.Any(x => string.Equals(x.TweakId, id, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add((id, data, item));
                    }
                }
            }
        }

        foreach (var (target, entries) in regValues)
        {
            if (entries.Count < 2) continue;

            for (int i = 0; i < entries.Count; i++)
            {
                for (int j = i + 1; j < entries.Count; j++)
                {
                    var a = entries[i];
                    var b = entries[j];

                    if (!string.Equals(a.Data, b.Data, StringComparison.Ordinal))
                    {
                        if (!HasOppositeMarking(a.TweakNode, a.TweakId, b.TweakNode, b.TweakId))
                        {
                            errors.Add($"Conflict on {target}: '{a.TweakId}' ({a.Data}) vs '{b.TweakId}' ({b.Data})");
                        }
                    }
                }
            }
        }

        return errors;
    }

    [Fact]
    public void AliasesLockFile_MatchesRealCatalog_Identically()
    {
        string baseDir = AppContext.BaseDirectory;
        string? current = baseDir;
        string? catalogDir = null;
        while (current != null)
        {
            string cand = Path.Combine(current, "Catalog");
            if (Directory.Exists(cand) && File.Exists(Path.Combine(cand, "aliases.lock.json")))
            {
                catalogDir = cand;
                break;
            }
            current = Directory.GetParent(current)?.FullName;
        }

        Assert.NotNull(catalogDir);
        string lockPath = Path.Combine(catalogDir!, "aliases.lock.json");
        Assert.True(File.Exists(lockPath), "Catalog/aliases.lock.json must exist.");

        var lockObj = JsonNode.Parse(File.ReadAllText(lockPath))!.AsObject();
        Assert.Equal(11, lockObj.Count);

        // Load all catalog tweaks
        var catalogMap = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(Path.Combine(catalogDir!, "Tweaks"), "*.json"))
        {
            var arr = JsonNode.Parse(File.ReadAllText(file))!.AsArray();
            foreach (var node in arr)
            {
                if (node != null && node["id"] != null)
                {
                    catalogMap[node["id"]!.ToString()] = node;
                }
            }
        }

        foreach (var (canonicalId, entryNode) in lockObj)
        {
            Assert.True(catalogMap.ContainsKey(canonicalId), $"Catalog missing locked canonical tweak '{canonicalId}'");
            var tweak = catalogMap[canonicalId];

            var lockedAliases = entryNode!["aliases"]!.AsArray().Select(x => x!.ToString()).OrderBy(x => x).ToList();
            var actualAliases = tweak["aliases"]!.AsArray().Select(x => x!.ToString()).OrderBy(x => x).ToList();
            Assert.Equal(lockedAliases, actualAliases);

            var (sig, hash) = ComputeApplySignature(tweak);
            Assert.Equal(entryNode["signature"]!.ToString(), sig);
            Assert.Equal(entryNode["sha256"]!.ToString(), hash);
        }
    }

    [Fact]
    public void ConflictRule_DetectsConflictingData_WhenOppositeOfMissing()
    {
        var tweakA = new JsonObject
        {
            ["id"] = "tweak.a",
            ["apply"] = new JsonArray
            {
                new JsonObject
                {
                    ["hive"] = "HKLM",
                    ["key"] = @"SOFTWARE\Policies\Microsoft\Windows\System",
                    ["valueName"] = "EnableActivityFeed",
                    ["data"] = 1
                }
            }
        };

        var tweakB = new JsonObject
        {
            ["id"] = "tweak.b",
            ["apply"] = new JsonArray
            {
                new JsonObject
                {
                    ["hive"] = "HKLM",
                    ["key"] = @"SOFTWARE\Policies\Microsoft\Windows\System",
                    ["valueName"] = "EnableActivityFeed",
                    ["data"] = 0
                }
            }
        };

        var map = new Dictionary<string, JsonNode>
        {
            ["tweak.a"] = tweakA,
            ["tweak.b"] = tweakB
        };

        var errors = CheckConflictingRegistryData(map);
        Assert.Single(errors);
        Assert.Contains("EnableActivityFeed", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConflictRule_Passes_WhenOppositeOfDeclared()
    {
        var tweakA = new JsonObject
        {
            ["id"] = "tweak.a",
            ["oppositeOf"] = "tweak.b",
            ["apply"] = new JsonArray
            {
                new JsonObject
                {
                    ["hive"] = "HKLM",
                    ["key"] = @"SOFTWARE\Policies\Microsoft\Windows\System",
                    ["valueName"] = "EnableActivityFeed",
                    ["data"] = 1
                }
            }
        };

        var tweakB = new JsonObject
        {
            ["id"] = "tweak.b",
            ["apply"] = new JsonArray
            {
                new JsonObject
                {
                    ["hive"] = "HKLM",
                    ["key"] = @"SOFTWARE\Policies\Microsoft\Windows\System",
                    ["valueName"] = "EnableActivityFeed",
                    ["data"] = 0
                }
            }
        };

        var map = new Dictionary<string, JsonNode>
        {
            ["tweak.a"] = tweakA,
            ["tweak.b"] = tweakB
        };

        var errors = CheckConflictingRegistryData(map);
        Assert.Empty(errors);
    }

    [Fact]
    public void ConflictRule_Passes_WhenBothSetSameData()
    {
        var tweakA = new JsonObject
        {
            ["id"] = "tweak.a",
            ["apply"] = new JsonArray
            {
                new JsonObject
                {
                    ["hive"] = "HKCU",
                    ["key"] = @"Software\Policies\Microsoft\Windows\Explorer",
                    ["valueName"] = "DisableSearchBoxSuggestions",
                    ["data"] = 1
                }
            }
        };

        var tweakB = new JsonObject
        {
            ["id"] = "tweak.b",
            ["apply"] = new JsonArray
            {
                new JsonObject
                {
                    ["hive"] = "HKCU",
                    ["key"] = @"Software\Policies\Microsoft\Windows\Explorer",
                    ["valueName"] = "DisableSearchBoxSuggestions",
                    ["data"] = 1
                }
            }
        };

        var map = new Dictionary<string, JsonNode>
        {
            ["tweak.a"] = tweakA,
            ["tweak.b"] = tweakB
        };

        var errors = CheckConflictingRegistryData(map);
        Assert.Empty(errors);
    }
}

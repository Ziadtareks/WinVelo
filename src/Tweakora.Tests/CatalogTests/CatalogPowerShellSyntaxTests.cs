using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Tweakora.Tests.CatalogTests;

public sealed class CatalogPowerShellSyntaxTests
{
    private sealed record ScriptEntry(string TweakId, string File, string Script);

    [Fact]
    public void AllCatalogPowerShellScripts_DoNotContainMissingExternalCmdlets()
    {
        string baseDir = AppContext.BaseDirectory;
        string tweaksDir = FindDirectoryUpwards(baseDir, Path.Combine("Catalog", "Tweaks"));
        Assert.True(Directory.Exists(tweaksDir), $"Tweaks directory not found at {tweaksDir}");

        var scripts = CollectScripts(tweaksDir);
        Assert.NotEmpty(scripts);

        foreach (var entry in scripts)
        {
            Assert.DoesNotContain("Invoke-WinUtilExplorerUpdate", entry.Script, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AllCatalogPowerShellScripts_PassAstSyntaxParsing()
    {
        string baseDir = AppContext.BaseDirectory;
        string tweaksDir = FindDirectoryUpwards(baseDir, Path.Combine("Catalog", "Tweaks"));
        Assert.True(Directory.Exists(tweaksDir), $"Tweaks directory not found at {tweaksDir}");

        var scripts = CollectScripts(tweaksDir);
        Assert.NotEmpty(scripts);

        // Serialize script list to temporary JSON file for parse-only AST checking via powershell.exe
        string tempJsonFile = Path.Combine(Path.GetTempPath(), $"tweakora_ast_test_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempJsonFile, JsonSerializer.Serialize(scripts));

            string parseRunnerScript =
                "$ErrorActionPreference = 'Stop';\r\n" +
                $"$items = Get-Content -Raw -Path '{tempJsonFile.Replace("'", "''")}' | ConvertFrom-Json;\r\n" +
                "$failures = @();\r\n" +
                "foreach ($item in $items) {\r\n" +
                "    $tokens = $null;\r\n" +
                "    $errors = $null;\r\n" +
                "    $null = [System.Management.Automation.Language.Parser]::ParseInput($item.Script, [ref]$tokens, [ref]$errors);\r\n" +
                "    if ($errors.Count -gt 0) {\r\n" +
                "        $failures += \"$($item.TweakId) in $($item.File): $($errors[0].Message)\";\r\n" +
                "    }\r\n" +
                "}\r\n" +
                "if ($failures.Count -gt 0) {\r\n" +
                "    $failures | ForEach-Object { [Console]::Error.WriteLine($_) };\r\n" +
                "    exit 1;\r\n" +
                "}\r\n";

            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(parseRunnerScript));

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            Assert.True(process.ExitCode == 0, $"PowerShell AST parse errors found:\r\n{stderr}\r\n{stdout}");
        }
        finally
        {
            if (File.Exists(tempJsonFile))
            {
                try { File.Delete(tempJsonFile); } catch { }
            }
        }
    }

    private static List<ScriptEntry> CollectScripts(string tweaksDir)
    {
        var scripts = new List<ScriptEntry>();
        string[] tweakFiles = Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories);

        foreach (string tweakFile in tweakFiles)
        {
            string fileName = Path.GetFileName(tweakFile);
            string json = File.ReadAllText(tweakFile);
            var array = JsonNode.Parse(json)?.AsArray();
            if (array == null) continue;

            foreach (var tweakNode in array)
            {
                if (tweakNode == null) continue;
                string tweakId = tweakNode["id"]?.GetValue<string>() ?? "unknown";

                void CheckActions(JsonNode? actionsNode)
                {
                    if (actionsNode is not JsonArray actions) return;
                    foreach (var act in actions)
                    {
                        if (act == null) continue;
                        string type = act["type"]?.GetValue<string>() ?? "";
                        if (type.Equals("PowerShell", StringComparison.OrdinalIgnoreCase) ||
                            type.Equals("Script", StringComparison.OrdinalIgnoreCase))
                        {
                            string? script = act["script"]?.GetValue<string>();
                            if (!string.IsNullOrWhiteSpace(script))
                            {
                                scripts.Add(new ScriptEntry(tweakId, fileName, script));
                            }
                        }
                    }
                }

                CheckActions(tweakNode["apply"]);
                CheckActions(tweakNode["undo"]);
            }
        }

        return scripts;
    }

    private static string FindDirectoryUpwards(string startDir, string relativePath)
    {
        string? current = startDir;
        while (current != null)
        {
            string candidate = Path.Combine(current, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        return Path.Combine(startDir, relativePath);
    }
}

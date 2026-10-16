using System.Text;
using System.Text.Json;
using Tweakora.Core.Models;

namespace Tweakora.Core.Profiles;

public sealed class TweakoraProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string Author { get; init; } = "Tweakora Community";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<string> TweakIds { get; init; } = new();
}

public sealed class ProfileManager
{
    public static List<TweakoraProfile> GetBuiltInProfiles()
    {
        return new List<TweakoraProfile>
        {
            new()
            {
                Id = "preset.recommended",
                Name = "Recommended Baseline",
                Description = "Safe, well-tested settings that balance privacy, system responsiveness, and clean UI without breaking any features.",
                TweakIds = new List<string>
                {
                    "privacy.telemetry.diagnostic-data",
                    "privacy.advertising.advertising-id",
                    "privacy.experiences.tailored-experiences",
                    "ui.explorer.classic-context-menu",
                    "ui.explorer.show-file-extensions",
                    "ui.explorer.show-hidden-files",
                    "performance.visual.disable-transparency",
                    "performance.network.delivery-optimization-simple",
                    "system.security.disable-remote-assistance",
                    "system.security.disable-autorun",
                    "network.optimization.limit-peering-lan"
                }
            },
            new()
            {
                Id = "preset.privacy",
                Name = "Privacy & Security Shield",
                Description = "Maximum telemetry reduction, sensor overrides, store app permission denials, and privacy-focused network policies.",
                TweakIds = new List<string>
                {
                    "privacy.telemetry.diagnostic-data",
                    "privacy.advertising.advertising-id",
                    "privacy.experiences.tailored-experiences",
                    "essential.activity",
                    "privacy.speech.online-speech-recognition",
                    "privacy.typing.inking-typing-personalization",
                    "privacy.feedback.feedback-prompts",
                    "privacy.reporting.windows-error-reporting",
                    "privacy.location.disable-sensor",
                    "privacy.consent.deny-location",
                    "privacy.consent.deny-diagnostics",
                    "system.security.disable-llmnr",
                    "network.dns.enable-doh"
                }
            },
            new()
            {
                Id = "preset.gaming",
                Name = "Gaming & Low-Latency",
                Description = "Optimizes Windows for gaming stability by disabling DVR background capture, Game Bar overlays, and unwanted throttling.",
                TweakIds = new List<string>
                {
                    "gaming.mode.enable",
                    "gaming.xbox.disable-game-bar",
                    "gaming.dvr.disable-background-recording",
                    "gaming.input.disable-mouse-acceleration",
                    "performance.visual.disable-animations",
                    "performance.ntfs.disable-last-access",
                    "performance.ntfs.disable-8dot3-names",
                    "network.optimization.limit-peering-lan"
                }
            },
            new()
            {
                Id = "preset.minimal",
                Name = "Minimalist Clean Desktop",
                Description = "Removes clutter from the Windows 11 desktop: hides Copilot, Widgets, Teams Chat, and Gallery nodes.",
                TweakIds = new List<string>
                {
                    "ai.copilot.disable-taskbar-button",
                    "ui.taskbar.disable-widgets-button",
                    "ui.taskbar.disable-chat-button",
                    "ui.taskbar.disable-task-view-button",
                    "ui.explorer.hide-gallery",
                    "ui.explorer.hide-recent-files",
                    "ui.search.disable-search-highlights",
                    "ui.system.disable-welcome-experience",
                    "ui.system.disable-tips-suggestions"
                }
            }
        };
    }

    public static string ExportProfileToJson(TweakoraProfile profile)
    {
        return JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
    }

    public static TweakoraProfile? ImportProfileFromJson(string json)
    {
        return JsonSerializer.Deserialize<TweakoraProfile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public static string ExportProfileToReg(TweakoraProfile profile, IEnumerable<TweakDefinition> tweaks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine($"; Tweakora Exported Profile: {profile.Name}");
        sb.AppendLine($"; Created on: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        var tweakLookup = tweaks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var tweakId in profile.TweakIds)
        {
            if (!tweakLookup.TryGetValue(tweakId, out var tweak)) continue;

            sb.AppendLine($"; Tweak: {tweak.Name}");
            foreach (var action in tweak.Apply)
            {
                if (string.Equals(action.Type, "Registry", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(action.Type, "Policy", StringComparison.OrdinalIgnoreCase))
                {
                    string fullKey = $"[{action.Hive}\\{action.Key}]";
                    sb.AppendLine(fullKey);

                    if (string.Equals(action.Operation, "Set", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(action.ValueName))
                    {
                        string valType = action.ValueKind?.ToLowerInvariant() switch
                        {
                            "dword" => $"dword:{(action.Data.HasValue && action.Data.Value.ValueKind == JsonValueKind.Number ? action.Data.Value.GetInt32() : (int.TryParse(action.Data?.ToString(), out var i) ? i : 0)):x8}",
                            "string" => $"\"{action.Data?.ToString()?.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
                            "qword" => $"hex(b):{(action.Data.HasValue && action.Data.Value.ValueKind == JsonValueKind.Number ? action.Data.Value.GetInt64().ToString("x16") : "0000000000000000")}",
                            _ => $"\"{action.Data}\""
                        };
                        sb.AppendLine($"\"{action.ValueName}\"={valType}");
                    }
                    else if (string.Equals(action.Operation, "DeleteValue", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(action.ValueName))
                    {
                        sb.AppendLine($"\"{action.ValueName}\"=-");
                    }
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    public static string ExportProfileToPowerShell(TweakoraProfile profile, IEnumerable<TweakDefinition> tweaks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# =========================================================");
        sb.AppendLine($"# Tweakora Unattended PowerShell Setup: {profile.Name}");
        sb.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("# Run with Administrator privileges");
        sb.AppendLine("# =========================================================");
        sb.AppendLine();
        sb.AppendLine("Write-Host 'Applying Tweakora Profile: " + profile.Name + "...' -ForegroundColor Cyan");
        sb.AppendLine();

        var tweakLookup = tweaks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var tweakId in profile.TweakIds)
        {
            if (!tweakLookup.TryGetValue(tweakId, out var tweak)) continue;

            sb.AppendLine($"# [{tweak.Category.ToUpperInvariant()}] {tweak.Name}");
            foreach (var action in tweak.Apply)
            {
                if (string.Equals(action.Type, "Registry", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(action.Type, "Policy", StringComparison.OrdinalIgnoreCase))
                {
                    string psHive = (action.Hive ?? "").ToUpperInvariant() switch
                    {
                        "HKCU" => "HKCU:",
                        "HKLM" => "HKLM:",
                        "HKCR" => "HKCR:",
                        _ => "HKCU:"
                    };
                    string fullPath = $"{psHive}\\{action.Key}";

                    sb.AppendLine($"if (!(Test-Path '{fullPath}')) {{ New-Item -Path '{fullPath}' -Force | Out-Null }}");
                    if (string.Equals(action.Operation, "Set", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(action.ValueName))
                    {
                        string psVal = action.ValueKind?.Equals("DWord", StringComparison.OrdinalIgnoreCase) == true
                            ? (action.Data.HasValue && action.Data.Value.ValueKind == JsonValueKind.Number ? action.Data.Value.GetInt32().ToString() : action.Data?.ToString() ?? "0")
                            : $"'{action.Data?.ToString()?.Replace("'", "''")}'";
                        sb.AppendLine($"Set-ItemProperty -Path '{fullPath}' -Name '{action.ValueName}' -Value {psVal} -Type {action.ValueKind ?? "DWord"} -Force");
                    }
                    else if (string.Equals(action.Operation, "DeleteValue", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(action.ValueName))
                    {
                        sb.AppendLine($"Remove-ItemProperty -Path '{fullPath}' -Name '{action.ValueName}' -ErrorAction SilentlyContinue");
                    }
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("Write-Host 'Profile successfully applied!' -ForegroundColor Green");
        return sb.ToString();
    }
}

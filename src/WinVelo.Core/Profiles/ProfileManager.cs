using System.Text;
using System.Text.Json;
using WinVelo.Core.Models;

namespace WinVelo.Core.Profiles;

public sealed class WinVeloProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string Author { get; init; } = "WinVelo Community";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<string> TweakIds { get; init; } = new();
}

public sealed class ProfileManager
{
    public static List<WinVeloProfile> GetBuiltInProfiles()
    {
        return new List<WinVeloProfile>
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

    public static string ExportProfileToJson(WinVeloProfile profile)
    {
        return JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
    }

    public static WinVeloProfile? ImportProfileFromJson(string json)
    {
        return JsonSerializer.Deserialize<WinVeloProfile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public static string ExportProfileToReg(WinVeloProfile profile, IEnumerable<TweakDefinition> tweaks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine($"; WinVelo Exported Profile: {profile.Name}");
        sb.AppendLine($"; Created on: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        var tweakLookup = tweaks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var tweakId in profile.TweakIds)
        {
            if (!tweakLookup.TryGetValue(tweakId, out var tweak)) { sb.AppendLine("; Missing tweak: " + tweakId); continue; }
            sb.AppendLine("; Tweak: " + tweak.Name.Replace("\r", " ").Replace("\n", " "));
            foreach (var action in tweak.Apply)
            {
                if (action.Type.Equals("Registry", StringComparison.OrdinalIgnoreCase) || action.Type.Equals("Policy", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine(RegistryExport.Format(action));
                else sb.AppendLine("; Omitted " + action.Type + " action. Apply the JSON configuration with WinVelo CLI for the complete operation.");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public static string ExportProfileToPowerShell(WinVeloProfile profile, IEnumerable<TweakDefinition> tweaks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# =========================================================");
        sb.AppendLine($"# WinVelo Unattended PowerShell Setup: {profile.Name}");
        sb.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("# Run with Administrator privileges");
        sb.AppendLine("# Registry-only export. Use WinVelo CLI --config for service, script, task and module actions.");
        sb.AppendLine("# =========================================================");
        sb.AppendLine();
        sb.AppendLine("Write-Host 'Applying WinVelo Profile: " + profile.Name.Replace("'", "''") + "...' -ForegroundColor Cyan");
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

        sb.AppendLine("Write-Host 'Registry portion applied. See the header for omitted action types.' -ForegroundColor Green");
        return sb.ToString();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Operations;

namespace Tweakora.Modules.Apps;

public sealed partial class ApplicationItem : ObservableObject
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Link { get; init; } = string.Empty;
    public string WingetId { get; init; } = string.Empty;
    public string ChocoId { get; init; } = string.Empty;

    public string BrandColor { get; init; } = "#3B82F6";
    public string IconSymbol { get; init; } = "Apps24";
    public string ShortCode { get; init; } = "AP";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Ready";

    public string GetPackageId(string packageManager)
    {
        return packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase)
            ? (!string.IsNullOrWhiteSpace(ChocoId) ? ChocoId : WingetId)
            : (!string.IsNullOrWhiteSpace(WingetId) ? WingetId : ChocoId);
    }
}

public interface IAppInstallService
{
    IReadOnlyList<ApplicationItem> LoadApplications();
    Task<OperationExecutionReport> InstallAppsAsync(
        IReadOnlyList<ApplicationItem> apps,
        string packageManager,
        IOperationRunner runner,
        CancellationToken cancellationToken = default);
    Task<OperationExecutionReport> UninstallAppsAsync(
        IReadOnlyList<ApplicationItem> apps,
        string packageManager,
        IOperationRunner runner,
        CancellationToken cancellationToken = default);
    Task<string> UpgradeAllAsync(string packageManager, CancellationToken cancellationToken = default);
    Task DetectInstalledStatusAsync(IEnumerable<ApplicationItem> apps, CancellationToken cancellationToken = default);
}

public sealed class AppInstallService : IAppInstallService
{
    private readonly IShellService _shell;

    public AppInstallService(IShellService shell)
    {
        _shell = shell;
    }

    public static (string Color, string Icon, string Code) GetBrandMeta(string key, string category)
    {
        return key.ToLowerInvariant() switch
        {
            "chrome" or "googlechrome" => ("#4285F4", "Globe24", "GC"),
            "brave" => ("#FB542B", "Globe24", "BR"),
            "firefox" or "mozillafirefox" => ("#FF7139", "Globe24", "FF"),
            "edge" or "microsoftedge" => ("#0078D7", "Globe24", "ME"),
            "discord" => ("#5865F2", "Chat24", "DC"),
            "telegram" => ("#26A5E4", "Send24", "TG"),
            "whatsapp" => ("#25D366", "Phone24", "WA"),
            "zoom" => ("#2D8CFF", "VideoChat24", "ZM"),
            "vlc" => ("#FF8800", "Video24", "VLC"),
            "spotify" => ("#1DB954", "MusicNote224", "SP"),
            "obs" or "obsstudio" => ("#302E31", "Record24", "OBS"),
            "handbrake" => ("#E05A47", "Video24", "HB"),
            "audacity" => ("#0000EB", "Speaker224", "AU"),
            "paintnet" => ("#0078D4", "DrawShape24", "PDN"),
            "sharex" => ("#00B4D8", "Crop24", "SX"),
            "7zip" => ("#2B579A", "FolderZip24", "7Z"),
            "winrar" => ("#8B5CF6", "FolderZip24", "RAR"),
            "powertoys" => ("#0078D4", "Wrench24", "PT"),
            "everything" => ("#E67E22", "FolderSearch24", "EV"),
            "wiztree" => ("#EAB308", "HardDrive24", "WT"),
            "qbittorrent" => ("#2B6CB0", "ArrowDownload24", "QB"),
            "anydesk" => ("#EF4444", "Desktop24", "AD"),
            "bitwarden" => ("#175DDC", "ShieldKeyhole24", "BW"),
            "bleachbit" => ("#22C55E", "Broom24", "BB"),
            "rufus" => ("#3B82F6", "UsbStick24", "RF"),
            "steam" => ("#171A21", "Games24", "ST"),
            "epicgames" => ("#2A2A2A", "Games24", "EG"),
            "goggalaxy" => ("#86269B", "Games24", "GOG"),
            "battlenet" => ("#00AEFF", "Games24", "BN"),
            "vscode" => ("#007ACC", "Code24", "VS"),
            "notepadplusplus" or "notepad++" => ("#90C53F", "DocumentText24", "NPP"),
            "git" => ("#F05032", "BranchFork24", "GIT"),
            "terminal" or "windowsterminal" => ("#4D4D4D", "WindowConsole20", "WT"),
            "python" or "python3" => ("#3776AB", "Code24", "PY"),
            "nodejs" or "node" => ("#339933", "Code24", "JS"),
            "githubdesktop" => ("#6E5494", "BranchFork24", "GH"),
            _ => category.ToLowerInvariant() switch
            {
                "browsers" => ("#3B82F6", "Globe24", key[..Math.Min(2, key.Length)].ToUpperInvariant()),
                "social" => ("#6366F1", "Chat24", key[..Math.Min(2, key.Length)].ToUpperInvariant()),
                "media" => ("#EC4899", "Video24", key[..Math.Min(2, key.Length)].ToUpperInvariant()),
                "gaming" => ("#8B5CF6", "Games24", key[..Math.Min(2, key.Length)].ToUpperInvariant()),
                "developer" => ("#10B981", "Code24", key[..Math.Min(2, key.Length)].ToUpperInvariant()),
                _ => ("#475569", "Apps24", key[..Math.Min(2, key.Length)].ToUpperInvariant())
            }
        };
    }

    public IReadOnlyList<ApplicationItem> LoadApplications()
    {
        string rootDir = AppDomain.CurrentDomain.BaseDirectory;
        string appJsonPath = Path.Combine(rootDir, "Catalog", "Apps", "applications.json");

        if (!File.Exists(appJsonPath))
        {
            string? parent = Directory.GetParent(rootDir)?.FullName;
            while (parent != null)
            {
                string candidate = Path.Combine(parent, "Catalog", "Apps", "applications.json");
                if (File.Exists(candidate))
                {
                    appJsonPath = candidate;
                    break;
                }
                parent = Directory.GetParent(parent)?.FullName;
            }
        }

        List<ApplicationItem> list = new();
        if (File.Exists(appJsonPath))
        {
            try
            {
                string json = File.ReadAllText(appJsonPath);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var elem = prop.Value;
                    string name = elem.TryGetProperty("name", out var nP) ? nP.GetString() ?? prop.Name : prop.Name;
                    string cat = elem.TryGetProperty("category", out var cP) ? cP.GetString() ?? "Utilities" : "Utilities";
                    string desc = elem.TryGetProperty("description", out var dP) ? dP.GetString() ?? "" : "";
                    string link = elem.TryGetProperty("link", out var lP) ? lP.GetString() ?? "" : "";
                    string winget = elem.TryGetProperty("winget", out var wP) ? wP.GetString() ?? "" : "";
                    string choco = elem.TryGetProperty("choco", out var chP) ? chP.GetString() ?? "" : "";

                    var (color, icon, code) = GetBrandMeta(prop.Name, cat);

                    list.Add(new ApplicationItem
                    {
                        Key = prop.Name,
                        Name = name,
                        Category = cat,
                        Description = desc,
                        Link = link,
                        WingetId = winget,
                        ChocoId = choco,
                        BrandColor = color,
                        IconSymbol = icon,
                        ShortCode = code
                    });
                }
            }
            catch { }
        }

        return list;
    }

    public async Task DetectInstalledStatusAsync(IEnumerable<ApplicationItem> apps, CancellationToken cancellationToken = default)
    {
        try
        {
            // Rapid detection of installed applications via winget list
            string output = await _shell.ExecutePowerShellAsync("winget list --accept-source-agreements", 30);
            if (!string.IsNullOrWhiteSpace(output))
            {
                foreach (var app in apps)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (!string.IsNullOrWhiteSpace(app.WingetId) && output.Contains(app.WingetId, StringComparison.OrdinalIgnoreCase))
                    {
                        app.IsInstalled = true;
                        app.StatusText = "Installed";
                    }
                    else if (output.Contains(app.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        app.IsInstalled = true;
                        app.StatusText = "Installed";
                    }
                }
            }
        }
        catch
        {
            // Fallback gracefully without throwing
        }
    }

    public async Task<OperationExecutionReport> InstallAppsAsync(
        IReadOnlyList<ApplicationItem> apps,
        string packageManager,
        IOperationRunner runner,
        CancellationToken cancellationToken = default)
    {
        return await runner.RunAsync(
            $"Install Applications ({packageManager})",
            apps,
            async (app, ct) =>
            {
                string pkgId = app.GetPackageId(packageManager);
                if (string.IsNullOrWhiteSpace(pkgId))
                {
                    return new OperationItemResult
                    {
                        Id = app.Key,
                        Name = app.Name,
                        Status = OperationItemStatus.Skipped,
                        Reason = $"No package ID available for {packageManager}."
                    };
                }

                app.IsBusy = true;
                app.StatusText = "Installing...";

                string script = packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase)
                    ? $"choco install \"{pkgId}\" -y --no-progress"
                    : $"winget install --id \"{pkgId}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

                try
                {
                    string output = await _shell.ExecutePowerShellAsync(script, 300);
                    bool success = !output.Contains("Error", StringComparison.OrdinalIgnoreCase) &&
                                   !output.Contains("Failed", StringComparison.OrdinalIgnoreCase) &&
                                   !output.Contains("Exit code: 1", StringComparison.OrdinalIgnoreCase);

                    if (success)
                    {
                        app.IsInstalled = true;
                        app.StatusText = "Installed";
                    }
                    else
                    {
                        app.StatusText = "Install Failed";
                    }

                    return new OperationItemResult
                    {
                        Id = app.Key,
                        Name = app.Name,
                        Status = success ? OperationItemStatus.Succeeded : OperationItemStatus.Failed,
                        Reason = success ? "Installed successfully." : output.Trim()
                    };
                }
                finally
                {
                    app.IsBusy = false;
                }
            },
            cancellationToken);
    }

    public async Task<OperationExecutionReport> UninstallAppsAsync(
        IReadOnlyList<ApplicationItem> apps,
        string packageManager,
        IOperationRunner runner,
        CancellationToken cancellationToken = default)
    {
        return await runner.RunAsync(
            $"Uninstall Applications ({packageManager})",
            apps,
            async (app, ct) =>
            {
                string pkgId = app.GetPackageId(packageManager);
                if (string.IsNullOrWhiteSpace(pkgId))
                {
                    return new OperationItemResult
                    {
                        Id = app.Key,
                        Name = app.Name,
                        Status = OperationItemStatus.Skipped,
                        Reason = $"No package ID available for {packageManager}."
                    };
                }

                app.IsBusy = true;
                app.StatusText = "Uninstalling...";

                string script = packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase)
                    ? $"choco uninstall \"{pkgId}\" -y"
                    : $"winget uninstall --id \"{pkgId}\" --exact --silent --disable-interactivity";

                try
                {
                    string output = await _shell.ExecutePowerShellAsync(script, 180);
                    bool success = !output.Contains("Error", StringComparison.OrdinalIgnoreCase) &&
                                   !output.Contains("Failed", StringComparison.OrdinalIgnoreCase);

                    if (success)
                    {
                        app.IsInstalled = false;
                        app.StatusText = "Uninstalled";
                    }
                    else
                    {
                        app.StatusText = "Uninstall Failed";
                    }

                    return new OperationItemResult
                    {
                        Id = app.Key,
                        Name = app.Name,
                        Status = success ? OperationItemStatus.Succeeded : OperationItemStatus.Failed,
                        Reason = success ? "Uninstalled successfully." : output.Trim()
                    };
                }
                finally
                {
                    app.IsBusy = false;
                }
            },
            cancellationToken);
    }

    public async Task<string> UpgradeAllAsync(string packageManager, CancellationToken cancellationToken = default)
    {
        string script = packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase)
            ? "choco upgrade all -y"
            : "winget upgrade --all --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

        return await _shell.ExecutePowerShellAsync(script, 600);
    }
}

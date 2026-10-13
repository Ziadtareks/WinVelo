using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Operations;

using CommunityToolkit.Mvvm.ComponentModel;

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
    public bool IsFoss { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isInstalled;

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
}

public sealed class AppInstallService : IAppInstallService
{
    private readonly IShellService _shell;

    public AppInstallService(IShellService shell)
    {
        _shell = shell;
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
                    string cat = elem.TryGetProperty("category", out var cP) ? cP.GetString() ?? "General" : "General";
                    string desc = elem.TryGetProperty("description", out var dP) ? dP.GetString() ?? "" : "";
                    string link = elem.TryGetProperty("link", out var lP) ? lP.GetString() ?? "" : "";
                    string winget = elem.TryGetProperty("winget", out var wP) ? wP.GetString() ?? "" : "";
                    string choco = elem.TryGetProperty("choco", out var chP) ? chP.GetString() ?? "" : "";
                    bool foss = elem.TryGetProperty("foss", out var fP) && fP.GetBoolean();

                    list.Add(new ApplicationItem
                    {
                        Key = prop.Name,
                        Name = name,
                        Category = cat,
                        Description = desc,
                        Link = link,
                        WingetId = winget,
                        ChocoId = choco,
                        IsFoss = foss
                    });
                }
            }
            catch { }
        }

        return list;
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

                string script = packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase)
                    ? $"choco install \"{pkgId}\" -y --no-progress"
                    : $"winget install --id \"{pkgId}\" --exact --silent --accept-package-agreements --accept-source-agreements";

                string output = await _shell.ExecutePowerShellAsync(script, 300);
                bool success = !output.Contains("Error", StringComparison.OrdinalIgnoreCase) &&
                               !output.Contains("Failed", StringComparison.OrdinalIgnoreCase);

                if (success) app.IsInstalled = true;

                return new OperationItemResult
                {
                    Id = app.Key,
                    Name = app.Name,
                    Status = success ? OperationItemStatus.Succeeded : OperationItemStatus.Failed,
                    Reason = success ? "Installed successfully." : output.Trim()
                };
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

                string script = packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase)
                    ? $"choco uninstall \"{pkgId}\" -y"
                    : $"winget uninstall --id \"{pkgId}\" --exact --silent";

                string output = await _shell.ExecutePowerShellAsync(script, 180);
                bool success = !output.Contains("Error", StringComparison.OrdinalIgnoreCase) &&
                               !output.Contains("Failed", StringComparison.OrdinalIgnoreCase);

                if (success) app.IsInstalled = false;

                return new OperationItemResult
                {
                    Id = app.Key,
                    Name = app.Name,
                    Status = success ? OperationItemStatus.Succeeded : OperationItemStatus.Failed,
                    Reason = success ? "Uninstalled successfully." : output.Trim()
                };
            },
            cancellationToken);
    }

    public async Task<string> UpgradeAllAsync(string packageManager, CancellationToken cancellationToken = default)
    {
        string script = packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase)
            ? "choco upgrade all -y"
            : "winget upgrade --all --silent --accept-package-agreements --accept-source-agreements";

        return await _shell.ExecutePowerShellAsync(script, 600);
    }
}

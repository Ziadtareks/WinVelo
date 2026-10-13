using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tweakora.Modules.Apps;

public sealed partial class AppItemViewModel : ObservableObject
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Link { get; init; } = string.Empty;
    public string? WinGetId { get; init; }
    public string? ChocoId { get; init; }
    public bool IsFoss { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isBusy;

    public string PackageId(string packageManager) =>
        packageManager.Equals("Chocolatey", StringComparison.OrdinalIgnoreCase)
            ? (ChocoId ?? WinGetId ?? string.Empty)
            : (WinGetId ?? ChocoId ?? string.Empty);
}

public sealed class AppCategoryGroup
{
    public required string CategoryName { get; init; }
    public List<AppItemViewModel> Apps { get; init; } = new();
}

public sealed class ApplicationCatalogManager
{
    public bool IsSimulationMode { get; set; } = true;

    public event Action<string, int>? OnProgress;
    public event Action<string>? OnLog;

    public List<AppItemViewModel> LoadCatalog()
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

        var apps = new List<AppItemViewModel>();

        if (File.Exists(appJsonPath))
        {
            string json = File.ReadAllText(appJsonPath);
            using var doc = JsonDocument.Parse(json);

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                string key = prop.Name;
                var elem = prop.Value;

                string name = elem.TryGetProperty("content", out var cP) ? cP.GetString() ?? key : key;
                string cat = elem.TryGetProperty("category", out var catP) ? catP.GetString() ?? "Utilities" : "Utilities";
                string desc = elem.TryGetProperty("description", out var dP) ? dP.GetString() ?? "" : "";
                string link = elem.TryGetProperty("link", out var lP) ? lP.GetString() ?? "" : "";
                string? winget = elem.TryGetProperty("winget", out var wP) && wP.ValueKind == JsonValueKind.String ? wP.GetString() : null;
                string? choco = elem.TryGetProperty("choco", out var chP) && chP.ValueKind == JsonValueKind.String ? chP.GetString() : null;
                bool foss = elem.TryGetProperty("foss", out var fP) && fP.GetBoolean();

                apps.Add(new AppItemViewModel
                {
                    Key = key,
                    Name = name,
                    Category = cat,
                    Description = desc,
                    Link = link,
                    WinGetId = winget,
                    ChocoId = choco,
                    IsFoss = foss
                });
            }
        }

        return apps.OrderBy(a => a.Category).ThenBy(a => a.Name).ToList();
    }

    public async Task InstallAppsAsync(IEnumerable<AppItemViewModel> apps, string packageManager = "WinGet")
    {
        var appList = apps.ToList();
        if (appList.Count == 0) return;

        OnLog?.Invoke($"[INFO] Starting installation of {appList.Count} app(s) via {packageManager} (Simulation Mode: {IsSimulationMode})...");

        for (int i = 0; i < appList.Count; i++)
        {
            var app = appList[i];
            app.IsBusy = true;
            int percent = (int)(((i + 1) / (double)appList.Count) * 100);
            OnProgress?.Invoke($"Installing {app.Name}...", percent);
            OnLog?.Invoke($"[INSTALL] {packageManager} install {app.PackageId(packageManager)} - ({app.Name})");

            if (IsSimulationMode)
            {
                await Task.Delay(50); // fast simulated progress
                app.IsInstalled = true;
            }

            app.IsBusy = false;
        }

        OnProgress?.Invoke("Completed", 100);
        OnLog?.Invoke($"[DONE] Finished installing {appList.Count} package(s).");
    }

    public async Task UninstallAppsAsync(IEnumerable<AppItemViewModel> apps, string packageManager = "WinGet")
    {
        var appList = apps.ToList();
        if (appList.Count == 0) return;

        OnLog?.Invoke($"[INFO] Starting uninstallation of {appList.Count} app(s) via {packageManager}...");

        for (int i = 0; i < appList.Count; i++)
        {
            var app = appList[i];
            app.IsBusy = true;
            int percent = (int)(((i + 1) / (double)appList.Count) * 100);
            OnProgress?.Invoke($"Uninstalling {app.Name}...", percent);
            OnLog?.Invoke($"[UNINSTALL] {packageManager} uninstall {app.PackageId(packageManager)} - ({app.Name})");

            if (IsSimulationMode)
            {
                await Task.Delay(50);
                app.IsInstalled = false;
                app.IsSelected = false;
            }

            app.IsBusy = false;
        }

        OnProgress?.Invoke("Completed", 100);
        OnLog?.Invoke($"[DONE] Finished uninstalling {appList.Count} package(s).");
    }
}

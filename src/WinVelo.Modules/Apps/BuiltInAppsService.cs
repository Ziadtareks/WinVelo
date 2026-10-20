using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinVelo.Core.Catalog;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Operations;
using WinVelo.Core.Utilities;

namespace WinVelo.Modules.Apps;

/// <summary>A built-in Windows (AppX) app from Catalog/Config/appx.json, WinUtil's list.</summary>
public sealed class BuiltInApp : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool? _isInstalled;

    /// <summary>WinUtil key, for example WPFAppxMicrosoft_BingNews (used in config files).</summary>
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    /// <summary>Exact AppX package name, for example Microsoft.BingNews.</summary>
    public required string PackageId { get; init; }
    /// <summary>Microsoft Store id used to reinstall, or empty when the Store has no listing.</summary>
    public string StoreId { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    /// <summary>WinVelo's own rating of what breaks when this app is removed: Safe, Low, Medium or High.</summary>
    public string Risk { get; init; } = "Low";
    /// <summary>What the user loses, shown before removal. Empty when nothing notable.</summary>
    public string Note { get; init; } = string.Empty;
    /// <summary>In WinVelo's "Select recommended": only apps that are safe to remove for almost everyone.</summary>
    public bool IsRecommended { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnChanged(); } }
    }

    /// <summary>True/false when known; null when it could not be checked (for example in simulation mode).</summary>
    public bool? IsInstalled
    {
        get => _isInstalled;
        set { if (_isInstalled != value) { _isInstalled = value; OnChanged(); OnChanged(nameof(InstalledText)); } }
    }

    public string InstalledText => IsInstalled switch { true => "Installed", false => "Not installed", _ => "Unknown" };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public interface IBuiltInAppsService
{
    IReadOnlyList<BuiltInApp> LoadApps();

    /// <summary>Names of the AppX packages installed for any user. Empty when this could not be read.</summary>
    Task<IReadOnlySet<string>?> GetInstalledPackagesAsync(CancellationToken cancellationToken = default);

    Task<OperationExecutionReport> RemoveAsync(IReadOnlyList<BuiltInApp> apps, IOperationRunner runner, CancellationToken cancellationToken = default);

    Task<OperationExecutionReport> ReinstallAsync(IReadOnlyList<BuiltInApp> apps, IOperationRunner runner, CancellationToken cancellationToken = default);
}

/// <summary>
/// Removes and reinstalls WinUtil's built-in apps. Differences from WinUtil, on purpose:
/// exact package names instead of "*Name*" wildcards (so "Microsoft.Paint" never also matches "Microsoft.Paint3D"),
/// no "kill every dllhost.exe" for Notepad, no "Uninstall-Package Microsoft Teams*" (it can remove Teams parts that
/// Office or IT installed), and every removal is written to a log so it can be reinstalled later.
/// </summary>
public sealed partial class BuiltInAppsService : IBuiltInAppsService
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.]{2,127}$")]
    private static partial Regex PackageNameRegex();

    private readonly IShellService _shell;
    private readonly CatalogSource _catalog;
    private readonly string _removalLog;

    /// <summary>WinVelo's risk rating and note per package. Anything not listed is Low with no note.</summary>
    private static readonly Dictionary<string, (string Risk, bool Recommended, string Note)> Ratings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.WindowsFeedbackHub"] = ("Safe", true, ""),
        ["Clipchamp.Clipchamp"] = ("Safe", true, ""),
        ["Microsoft.MicrosoftOfficeHub"] = ("Safe", true, ""),
        ["Microsoft.Windows.DevHome"] = ("Safe", true, ""),
        ["Microsoft.PowerAutomateDesktop"] = ("Safe", true, ""),
        ["Microsoft.BingNews"] = ("Safe", true, ""),
        ["Microsoft.BingWeather"] = ("Safe", true, ""),
        ["Microsoft.Copilot"] = ("Safe", true, ""),
        ["Microsoft.MicrosoftSolitaireCollection"] = ("Safe", true, "The Store has no listing for it, so it may not be possible to reinstall."),
        ["Microsoft.BingSearch"] = ("Low", false, ""),
        ["Microsoft.StartExperiencesApp"] = ("Low", false, ""),
        ["Microsoft.GetHelp"] = ("Low", false, "Windows troubleshooters and 'Get help' links open this app."),
        ["MicrosoftCorporationII.QuickAssist"] = ("Low", false, "Family or IT helpers often use Quick Assist to help you remotely."),
        ["Microsoft.GamingApp"] = ("Low", false, "PC Game Pass games are installed and managed through the Xbox app."),
        ["Microsoft.XboxGamingOverlay"] = ("Low", false, "Win+G, game recording and the Game Bar stop working."),
        ["Microsoft.XboxSpeechToTextOverlay"] = ("Low", false, "The Store has no listing for it, so it may not be possible to reinstall."),
        ["Microsoft.WindowsAlarms"] = ("Low", false, "Alarms and timers stop working."),
        ["Microsoft.WindowsSoundRecorder"] = ("Low", false, ""),
        ["MSTeams"] = ("Medium", false, "Only the Teams app is removed. On a work PC, ask your IT team first."),
        ["Microsoft.ZuneMusic"] = ("Medium", false, "This is the default music and video player. Install another player first."),
        ["Microsoft.MicrosoftStickyNotes"] = ("Medium", false, "Notes that are not synced to a Microsoft account are deleted."),
        ["Microsoft.Paint"] = ("Medium", false, ""),
        ["Microsoft.WindowsNotepad"] = ("Medium", false, "Notepad is the default editor for .txt files."),
        ["Microsoft.ScreenSketch"] = ("Medium", false, "Win+Shift+S and the Print Screen key use the Snipping Tool."),
        ["Microsoft.WindowsCalculator"] = ("Medium", false, ""),
        ["Microsoft.Windows.Photos"] = ("Medium", false, "Photos is the default image viewer. Install another image viewer first."),
        ["Microsoft.Xbox.TCUI"] = ("Medium", false, "Games that use Xbox sign-in, friends or invites can break. The Store has no listing to reinstall it."),
        ["Microsoft.XboxIdentityProvider"] = ("High", false, "Xbox sign-in for PC games (Store, Game Pass, Minecraft and others) depends on it, and removing it frees almost no space.")
    };

    public BuiltInAppsService(IShellService shell, string removalLogPath) : this(shell, CatalogSource.Default, removalLogPath)
    {
    }

    public BuiltInAppsService(IShellService shell, CatalogSource catalog, string removalLogPath)
    {
        _shell = shell;
        _catalog = catalog;
        _removalLog = removalLogPath;
    }

    public IReadOnlyList<BuiltInApp> LoadApps()
    {
        var list = new List<BuiltInApp>();
        using var doc = CatalogLoader.LoadDocument(_catalog, "Config/appx.json");
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return list;

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var v = prop.Value;
            if (v.ValueKind != JsonValueKind.Object) continue;
            string Get(string name) => v.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString()!.Trim() : string.Empty;

            string packageId = Get("PackageId");
            if (!PackageNameRegex().IsMatch(packageId)) continue;

            string storeId = Get("StoreId");
            if (!PackageIds.IsStoreId(storeId)) storeId = string.Empty;

            var rating = Ratings.TryGetValue(packageId, out var r) ? r : ("Low", false, string.Empty);
            list.Add(new BuiltInApp
            {
                Key = prop.Name,
                Name = Get("Content"),
                Description = Get("Description"),
                PackageId = packageId,
                StoreId = storeId,
                Category = Get("Category"),
                Risk = rating.Item1,
                IsRecommended = rating.Item2,
                Note = rating.Item3
            });
        }
        return list;
    }

    public async Task<IReadOnlySet<string>?> GetInstalledPackagesAsync(CancellationToken cancellationToken = default)
    {
        const string script = "Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name | Sort-Object -Unique";
        var res = await _shell.ExecutePowerShellAsync(script, 120);
        if (!res.Success || string.IsNullOrWhiteSpace(res.StdOut)) return null;
        var names = res.StdOut
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => PackageNameRegex().IsMatch(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // No package names at all means the list could not be read (a real PC always has some).
        return names.Count == 0 ? null : names;
    }

    /// <summary>The PowerShell that removes one package for all users and from the Windows image (so new users do not get it).</summary>
    public static string RemovalScript(string packageId)
    {
        if (!PackageNameRegex().IsMatch(packageId))
        {
            throw new ArgumentException($"'{packageId}' is not a valid AppX package name.", nameof(packageId));
        }

        string stop = packageId.Equals("Microsoft.XboxGamingOverlay", StringComparison.OrdinalIgnoreCase)
            ? "Stop-Process -Name GameBarFTServer -Force -ErrorAction SilentlyContinue\n"
            : string.Empty;

        return stop +
               $"Get-AppxPackage -AllUsers -Name '{packageId}' | Remove-AppxPackage -AllUsers -ErrorAction Stop\n" +
               $"Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq '{packageId}' | Remove-AppxProvisionedPackage -Online -AllUsers -ErrorAction Stop | Out-Null";
    }

    /// <summary>Re-registers the package from a copy Windows still has on disk; prints the manifest path when it worked.</summary>
    public static string ReRegisterScript(string packageId)
    {
        if (!PackageNameRegex().IsMatch(packageId))
        {
            throw new ArgumentException($"'{packageId}' is not a valid AppX package name.", nameof(packageId));
        }

        return "$m = @(Get-AppxPackage -AllUsers -Name '" + packageId + "' -ErrorAction SilentlyContinue | Where-Object InstallLocation | ForEach-Object { Join-Path $_.InstallLocation 'AppxManifest.xml' }) + " +
               "@(Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Where-Object DisplayName -eq '" + packageId + "' | Where-Object InstallLocation | ForEach-Object { Join-Path $_.InstallLocation 'AppxManifest.xml' }) | " +
               "Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1\n" +
               "if ($m) { Add-AppxPackage -Register $m -DisableDevelopmentMode -ErrorAction Stop; Write-Output $m } else { exit 2 }";
    }

    public Task<OperationExecutionReport> RemoveAsync(IReadOnlyList<BuiltInApp> apps, IOperationRunner runner, CancellationToken cancellationToken = default) =>
        runner.RunAsync("Remove built-in apps", apps, async (app, ct) =>
        {
            var res = await _shell.ExecutePowerShellAsync(RemovalScript(app.PackageId), 300);
            if (res.Success)
            {
                AppendRemovalLog(app);
                app.IsInstalled = false;
                return Result(app, OperationItemStatus.Succeeded, $"Removed {app.Name}.");
            }

            string detail = string.IsNullOrWhiteSpace(res.StdErr) ? $"exit code {res.ExitCode}" : res.StdErr.Trim();
            return Result(app, OperationItemStatus.Failed, $"Could not remove {app.Name}: {detail}");
        }, cancellationToken);

    public Task<OperationExecutionReport> ReinstallAsync(IReadOnlyList<BuiltInApp> apps, IOperationRunner runner, CancellationToken cancellationToken = default) =>
        runner.RunAsync("Reinstall built-in apps", apps, async (app, ct) =>
        {
            // 1. A copy is often still on disk (in the Windows image): register it, no download needed.
            var local = await _shell.ExecutePowerShellAsync(ReRegisterScript(app.PackageId), 300);
            if (local.Success)
            {
                app.IsInstalled = true;
                return Result(app, OperationItemStatus.Succeeded, $"Reinstalled {app.Name} from the copy on this PC.");
            }

            // 2. Otherwise install it from the Microsoft Store with winget.
            if (string.IsNullOrEmpty(app.StoreId))
            {
                return Result(app, OperationItemStatus.Failed, $"{app.Name} has no Microsoft Store listing and no copy on this PC, so it cannot be reinstalled.");
            }

            var args = PackageCommands.Arguments("Winget", PackageAction.Install, app.StoreId);
            var store = await _shell.RunProcessAsync("winget.exe", args, 900, ct);
            var outcome = PackageCommands.Interpret("Winget", PackageAction.Install, store);
            if (store.Cancelled)
            {
                return Result(app, OperationItemStatus.Skipped, "Cancelled.");
            }

            // "Already installed" comes back as Skipped: the app is there, which is what was asked for.
            if (outcome.Status is OperationItemStatus.Succeeded or OperationItemStatus.Applied or OperationItemStatus.Skipped)
            {
                app.IsInstalled = true;
                return Result(app, OperationItemStatus.Succeeded, $"Reinstalled {app.Name} from the Microsoft Store.");
            }

            return Result(app, OperationItemStatus.Failed, $"Could not reinstall {app.Name}: {outcome.Message}");
        }, cancellationToken);

    private static OperationItemResult Result(BuiltInApp app, OperationItemStatus status, string reason) =>
        new() { Id = app.Key, Name = app.Name, Status = status, Reason = reason };

    private void AppendRemovalLog(BuiltInApp app)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_removalLog)!);
            string line = JsonSerializer.Serialize(new { removedAtUtc = DateTime.UtcNow, key = app.Key, packageId = app.PackageId, storeId = app.StoreId });
            File.AppendAllText(_removalLog, line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Keys of apps WinVelo removed earlier (newest last), read from the removal log.</summary>
    public IReadOnlyList<string> RemovedKeys()
    {
        var keys = new List<string>();
        try
        {
            if (!File.Exists(_removalLog)) return keys;
            foreach (string line in File.ReadAllLines(_removalLog))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("key", out var k) && k.GetString() is { } key) keys.Add(key);
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return keys;
    }
}

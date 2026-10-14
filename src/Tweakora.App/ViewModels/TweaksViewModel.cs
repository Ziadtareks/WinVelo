using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.App.Services;
using Tweakora.Core.Engine.Runners;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Core.Safety;
using Tweakora.Modules.Tweaks;
using Tweakora.Platform;

namespace Tweakora.App.ViewModels;

public sealed partial class LogEntryItem : ObservableObject
{
    public required string Timestamp { get; init; }
    public required string Level { get; init; } // "OK", "SKIP", "WARN", "ERR", "INFO"
    public required string Message { get; init; }

    public string BadgeBg => Level switch
    {
        "OK" => "#065F46",
        "SKIP" => "#1E3A8A",
        "WARN" => "#78350F",
        "ERR" => "#7F1D1D",
        _ => "#374151"
    };

    public string BadgeFg => Level switch
    {
        "OK" => "#34D399",
        "SKIP" => "#60A5FA",
        "WARN" => "#FBBF24",
        "ERR" => "#F87171",
        _ => "#D1D5DB"
    };
}

public sealed partial class TweakCardModel : ObservableObject
{
    public TweakDefinition Tweak { get; }

    public string Id => Tweak.Id;
    public string? Origin => Tweak.Origin;
    public string Title => Tweak.Name;
    public string Description => Tweak.Description;
    public string Category => Tweak.Category;
    public string? Subcategory => Tweak.Subcategory;
    public string Risk => Tweak.Risk;
    public bool RequiresReboot => Tweak.RequiresReboot;
    public bool RequiresExplorerRestart => Tweak.RequiresExplorerRestart;
    public string? Warnings => Tweak.Warnings;
    public string? EffectNote => Tweak.EffectNote;
    public bool HasWarning => !string.IsNullOrWhiteSpace(Warnings);
    public bool HasEffectNote => !string.IsNullOrWhiteSpace(EffectNote);
    public bool IsBitLocker => Id.Contains("bitlocker", StringComparison.OrdinalIgnoreCase) ||
                               (Origin?.Contains("bitlocker", StringComparison.OrdinalIgnoreCase) ?? false);

    // Compatibility aliases for existing UI bindings
    public string LocalizedName => Title;
    public string LocalizedDescription => Description;
    public string? LocalizedWarning => Warnings;
    public string? LocalizedEffectNote => EffectNote;
    public bool IsHighRisk => string.Equals(Risk, "High", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(Risk, "Caution", StringComparison.OrdinalIgnoreCase);

    public string CategoryGroup { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeBackground))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeForeground))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeBorder))]
    private bool _isActive;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotApplying))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeBackground))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeForeground))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeBorder))]
    private bool _isApplying;

    public bool IsNotApplying => !IsApplying;

    public string StatusBadgeText => IsApplying ? "APPLYING..." : (IsActive ? "APPLIED ✓" : "DEFAULT");
    public string StatusBadgeBackground => IsApplying ? "#1E3A8A" : (IsActive ? "#064E3B" : "#1E222D");
    public string StatusBadgeForeground => IsApplying ? "#60A5FA" : (IsActive ? "#10B981" : "#94A3B8");
    public string StatusBadgeBorder => IsApplying ? "#3B82F6" : (IsActive ? "#059669" : "#334155");

    public string RiskBadgeBackground => Risk switch
    {
        "Safe" => "#064E3B",
        "Low" or "Moderate" or "Medium" => "#451A03",
        "Caution" or "High" => "#450A0A",
        _ => "#1E222D"
    };

    public string RiskBadgeForeground => Risk switch
    {
        "Safe" => "#10B981",
        "Low" or "Moderate" or "Medium" => "#F59E0B",
        "Caution" or "High" => "#EF4444",
        _ => "#94A3B8"
    };

    public string RiskBadgeBorder => Risk switch
    {
        "Safe" => "#059669",
        "Low" or "Moderate" or "Medium" => "#D97706",
        "Caution" or "High" => "#DC2626",
        _ => "#334155"
    };

    public TweakCardModel(TweakDefinition tweak)
    {
        Tweak = tweak;
        CategoryGroup = ClassifyCategory(tweak);
    }

    private static string ClassifyCategory(TweakDefinition tweak)
    {
        string sub = tweak.Subcategory ?? "";
        string id = tweak.Id.ToLowerInvariant();
        string cat = tweak.Category.ToLowerInvariant();
        string risk = tweak.Risk.ToLowerInvariant();

        if (risk == "high" || risk == "caution" || sub.Contains("Advanced", StringComparison.OrdinalIgnoreCase) || id.StartsWith("advanced."))
        {
            return "Advanced";
        }
        if (sub.Contains("Essential", StringComparison.OrdinalIgnoreCase) || id.StartsWith("essential."))
        {
            return "Essential";
        }
        if (cat.Contains("privacy") || tweak.Impacts.Any(i => i.Contains("privacy", StringComparison.OrdinalIgnoreCase)) ||
            id.Contains("telemetry") || id.Contains("tracking") || id.Contains("cortana") || id.Contains("diagnostic") || id.Contains("activity"))
        {
            return "Privacy";
        }
        if (cat.Contains("performance") || cat.Contains("system") || tweak.Impacts.Any(i => i.Contains("performance", StringComparison.OrdinalIgnoreCase)) ||
            id.Contains("hiber") || id.Contains("memory") || id.Contains("cache") || id.Contains("speed") || id.Contains("network"))
        {
            return "Performance";
        }
        if (cat.Contains("ui") || cat.Contains("preferences") || id.StartsWith("pref.") || sub.Contains("Customize", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("darkmode") || id.Contains("taskbar") || id.Contains("start") || id.Contains("snap") || id.Contains("ribbon"))
        {
            return "Customize UI";
        }

        return "Performance";
    }
}

public sealed partial class TweaksViewModel : ObservableObject
{
    private readonly ITweakService _tweakService;
    private readonly IOperationRunner _runner;
    private readonly List<TweakCardModel> _allTweaks = new();
    private readonly Dictionary<string, List<string>> _presets = new(StringComparer.OrdinalIgnoreCase);

    // Primary modern display collection
    public ObservableCollection<TweakCardModel> FilteredTweaks { get; } = new();

    // Legacy multi-column collections maintained for compatibility
    public ObservableCollection<TweakCardModel> EssentialTweaks { get; } = new();
    public ObservableCollection<TweakCardModel> AdvancedTweaks { get; } = new();
    public ObservableCollection<TweakCardModel> PreferenceToggles { get; } = new();
    public ObservableCollection<TweakCardModel> ExtraTweaks { get; } = new();

    // Live Execution Log Drawer items
    public ObservableCollection<LogEntryItem> LogEntries { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private int _appliedCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _allCount;

    [ObservableProperty]
    private int _essentialCount;

    [ObservableProperty]
    private int _privacyCount;

    [ObservableProperty]
    private int _performanceCount;

    [ObservableProperty]
    private int _customizeUiCount;

    [ObservableProperty]
    private int _advancedCount;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private int _scanProgress;

    [ObservableProperty]
    private string _scanStatusText = "Ready";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDryRun;

    [ObservableProperty]
    private bool _isLogDrawerOpen;

    [ObservableProperty]
    private double _executionProgress;

    [ObservableProperty]
    private string _statusMessage = "Select tweaks or choose a preset to begin.";

    [ObservableProperty]
    private bool _isPreviewDialogOpen;

    [ObservableProperty]
    private string _previewReportText = string.Empty;

    public bool IsAdmin => AdminPrivilegeHelper.IsRunningAsAdministrator();

    private readonly IActivityTracker? _activityTracker;

    public TweaksViewModel(ITweakService tweakService, IOperationRunner runner, IActivityTracker? activityTracker = null)
    {
        _tweakService = tweakService;
        _runner = runner;
        _activityTracker = activityTracker;

        LoadPresets();
        LoadCatalogTweaksFast();

        // Start non-blocking real-time detection scan in background
        _ = Task.Run(() => ScanSystemStateAsync());
    }

    private void LoadPresets()
    {
        string rootDir = AppDomain.CurrentDomain.BaseDirectory;
        string presetPath = Path.Combine(rootDir, "Catalog", "Config", "preset.json");

        if (!File.Exists(presetPath))
        {
            string? parent = Directory.GetParent(rootDir)?.FullName;
            while (parent != null)
            {
                string candidate = Path.Combine(parent, "Catalog", "Config", "preset.json");
                if (File.Exists(candidate))
                {
                    presetPath = candidate;
                    break;
                }
                parent = Directory.GetParent(parent)?.FullName;
            }
        }

        if (File.Exists(presetPath))
        {
            try
            {
                string json = File.ReadAllText(presetPath);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var list = new List<string>();
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        string? s = item.GetString();
                        if (!string.IsNullOrEmpty(s)) list.Add(s);
                    }
                    _presets[prop.Name] = list;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load presets: {ex.Message}");
            }
        }
    }

    private void LoadCatalogTweaksFast()
    {
        _allTweaks.Clear();
        var catalog = _tweakService.LoadCatalog();

        foreach (var tweak in catalog)
        {
            var card = new TweakCardModel(tweak);
            card.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TweakCardModel.IsSelected))
                {
                    UpdateCounts();
                }
            };
            _allTweaks.Add(card);
        }

        TotalCount = _allTweaks.Count;
        AllCount = TotalCount;
        EssentialCount = _allTweaks.Count(t => t.CategoryGroup == "Essential");
        PrivacyCount = _allTweaks.Count(t => t.CategoryGroup == "Privacy");
        PerformanceCount = _allTweaks.Count(t => t.CategoryGroup == "Performance");
        CustomizeUiCount = _allTweaks.Count(t => t.CategoryGroup == "Customize UI");
        AdvancedCount = _allTweaks.Count(t => t.CategoryGroup == "Advanced");

        ApplyFilter();
    }

    public async Task RefreshActiveStatesAsync(CancellationToken cancellationToken = default)
    {
        if (IsScanning) return;
        await ScanSystemStateAsync(cancellationToken);
    }

    public async Task ScanSystemStateAsync(CancellationToken cancellationToken = default)
    {
        IsScanning = true;
        ScanProgress = 0;
        ScanStatusText = "Scanning system tweaks...";

        await Task.Yield();
        int count = _allTweaks.Count;

        await Task.Run(async () =>
        {
            for (int i = 0; i < count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var card = _allTweaks[i];
                try
                {
                    bool active = await _tweakService.DetectStateAsync(card.Tweak, cancellationToken);
                    if (card.IsActive != active)
                    {
                        Application.Current?.Dispatcher?.InvokeAsync(() =>
                        {
                            card.IsActive = active;
                        });
                    }
                }
                catch { }

                int currentIdx = i + 1;
                int pct = (int)((currentIdx / (double)count) * 100);

                if (currentIdx % 10 == 0 || currentIdx == count)
                {
                    Application.Current?.Dispatcher?.InvokeAsync(() =>
                    {
                        ScanProgress = pct;
                        ScanStatusText = $"Scanning ({currentIdx}/{count}): {card.Title}";
                        AppliedCount = _allTweaks.Count(t => t.IsActive);
                    });
                }
            }
        }, cancellationToken);

        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            IsScanning = false;
            ScanProgress = 100;
            AppliedCount = _allTweaks.Count(t => t.IsActive);
            ScanStatusText = $"Scan complete. {AppliedCount} applied on system.";
            AddLog("OK", $"System state detection finished. Currently applied: {AppliedCount}/{count} tweaks.");
        });
    }

    [ObservableProperty]
    private bool _isBeginnerMode = false;

    public void SetSearchQuery(string query)
    {
        SearchQuery = query;
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnIsBeginnerModeChanged(bool value) => ApplyFilter();

    [RelayCommand]
    public void SetMode(string mode)
    {
        IsBeginnerMode = string.Equals(mode, "Beginner", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    public void SelectCategory(string category)
    {
        SelectedCategory = category;
    }

    public void ApplyFilter()
    {
        FilteredTweaks.Clear();
        EssentialTweaks.Clear();
        AdvancedTweaks.Clear();
        PreferenceToggles.Clear();
        ExtraTweaks.Clear();

        string q = SearchQuery?.Trim() ?? string.Empty;
        string cat = SelectedCategory ?? "All";

        foreach (var card in _allTweaks)
        {
            // Beginner mode filter: hide caution / high-risk tweaks
            if (IsBeginnerMode)
            {
                if (card.IsHighRisk || string.Equals(card.Risk, "Caution", StringComparison.OrdinalIgnoreCase) || card.CategoryGroup == "Advanced")
                {
                    continue;
                }
            }

            // Category filter
            if (!string.Equals(cat, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(card.CategoryGroup, cat, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            // Search query filter
            if (!string.IsNullOrWhiteSpace(q))
            {
                bool matches = card.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               card.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               card.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               (card.Origin?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
                if (!matches) continue;
            }

            FilteredTweaks.Add(card);

            // Legacy collections for multi-column compatibility
            if (card.CategoryGroup == "Essential") EssentialTweaks.Add(card);
            else if (card.CategoryGroup == "Advanced") AdvancedTweaks.Add(card);
            else if (card.CategoryGroup == "Customize UI") PreferenceToggles.Add(card);
            else ExtraTweaks.Add(card);
        }
    }

    [RelayCommand]
    public async Task ToggleTweakAsync(TweakCardModel card)
    {
        if (card == null || card.IsBusy || card.IsApplying) return;
        card.IsBusy = true;
        card.IsApplying = true;
        bool targetState = !card.IsActive;

        string verb = targetState ? "Applying" : "Reverting";
        using var activity = _activityTracker?.BeginActivity($"{verb} {card.Title}...");
        await Task.Yield();

        try
        {
            var (report, actualActive) = await Task.Run(async () =>
            {
                OperationExecutionReport rep;
                if (targetState)
                {
                    rep = await _tweakService.ApplyTweaksAsync(new[] { card.Tweak }, _runner);
                }
                else
                {
                    rep = await _tweakService.UndoTweaksAsync(new[] { card.Tweak }, _runner);
                }
                _ = WindowsSettingNotifier.NotifySettingsChangedAsync();
                bool active = await _tweakService.DetectStateAsync(card.Tweak);
                return (rep, active);
            });

            card.IsActive = actualActive;
            AppliedCount = _allTweaks.Count(t => t.IsActive);

            var res = report.Results.FirstOrDefault();
            if (res != null && (res.Status == OperationItemStatus.Succeeded || actualActive == targetState))
            {
                AddLog("OK", $"Successfully {(actualActive ? "applied" : "reverted")}: {card.Title}");
            }
            else
            {
                AddLog("ERR", $"Notice for {card.Title}: {res?.Reason ?? "System policy updated"}");
            }
            UpdateCounts();
        }
        catch (Exception ex)
        {
            AddLog("ERR", $"Error toggling {card.Title}: {ex.Message}");
        }
        finally
        {
            card.IsApplying = false;
            card.IsBusy = false;
        }
    }

    private void UpdateCounts()
    {
        SelectedCount = _allTweaks.Count(t => t.IsSelected);
        AppliedCount = _allTweaks.Count(t => t.IsActive);
    }

    [RelayCommand]
    public void ApplyPreset(string presetName)
    {
        if (string.Equals(presetName, "Clear", StringComparison.OrdinalIgnoreCase))
        {
            ClearSelection();
            return;
        }

        if (string.Equals(presetName, "SelectApplied", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var card in _allTweaks)
            {
                card.IsSelected = card.IsActive;
            }
            UpdateCounts();
            StatusMessage = $"Selected {SelectedCount} currently applied tweaks.";
            AddLog("INFO", $"Selected all currently applied tweaks ({SelectedCount} total).");
            return;
        }

        // Map preset names: "Standard" -> "recommended", "Minimal" -> "minimal"
        string mappedKey = presetName.ToLowerInvariant() switch
        {
            "standard" => "recommended",
            "minimal" => "minimal",
            "advanced" => "advanced",
            _ => presetName
        };

        if (!_presets.TryGetValue(mappedKey, out var presetKeys) &&
            !_presets.TryGetValue(presetName, out presetKeys))
        {
            StatusMessage = $"Preset '{presetName}' not found.";
            return;
        }

        var keySet = new HashSet<string>(presetKeys, StringComparer.OrdinalIgnoreCase);
        int matched = 0;

        foreach (var card in _allTweaks)
        {
            if (card.IsBitLocker)
            {
                card.IsSelected = false;
                continue;
            }

            bool matches = (card.Origin != null && keySet.Contains(card.Origin)) ||
                           keySet.Contains(card.Id);

            card.IsSelected = matches;
            if (matches) matched++;
        }

        UpdateCounts();
        StatusMessage = $"Preset '{presetName}' loaded ({matched} tweaks selected).";
        AddLog("INFO", $"Applied preset '{presetName}' ({matched} tweaks selected).");
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var card in _allTweaks)
        {
            card.IsSelected = false;
        }
        UpdateCounts();
        StatusMessage = "Selection cleared.";
        AddLog("INFO", "Cleared all selected tweaks.");
    }

    [RelayCommand]
    public async Task ApplySelectedTweaksAsync()
    {
        var selected = _allTweaks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No tweaks selected. Select tweaks or choose a preset first.";
            AddLog("WARN", "Apply requested but no tweaks were selected.");
            return;
        }

        if (!IsDryRun && !AppAdminGuard.EnsureAdmin("apply system tweaks"))
        {
            StatusMessage = "Administrator privileges required to apply tweaks.";
            AddLog("ERR", "Cannot apply tweaks: Administrator privileges are required.");
            return;
        }

        IsLogDrawerOpen = true;
        IsBusy = true;
        ExecutionProgress = 0;

        foreach (var card in selected)
        {
            card.IsApplying = true;
        }

        await Task.Yield();

        try
        {
            if (IsDryRun)
            {
                AddLog("INFO", $"--- DRY-RUN MODE: Simulating apply for {selected.Count} tweak(s) ---");
                for (int i = 0; i < selected.Count; i++)
                {
                    var tweak = selected[i];
                    AddLog("SKIP", $"[DRY-RUN] Would apply: {tweak.Title} ({tweak.Id})");
                    ExecutionProgress = ((i + 1) / (double)selected.Count) * 100;
                }
                StatusMessage = $"Dry-run completed for {selected.Count} tweak(s). No changes made.";
                AddLog("OK", "Dry-run simulation completed successfully.");
                return;
            }

            AddLog("INFO", $"Starting application of {selected.Count} tweak(s)...");
            AddLog("INFO", "Creating automatic pre-tweak backup snapshot...");

            var tweakDefs = selected.Select(c => c.Tweak).ToList();
            var report = await Task.Run(async () => await _tweakService.ApplyTweaksAsync(tweakDefs, _runner));

            foreach (var item in report.Results)
            {
                string level = item.Status switch
                {
                    OperationItemStatus.Succeeded => "OK",
                    OperationItemStatus.Skipped => "SKIP",
                    _ => "ERR"
                };
                AddLog(level, $"{item.Name}: {item.Reason}");
            }

            // Update state
            foreach (var card in selected)
            {
                card.IsActive = true;
            }

            UpdateCounts();
            StatusMessage = $"Applied {report.SucceededCount} tweak(s), {report.SkippedCount} skipped, {report.FailedCount} failed.";
            AddLog("OK", $"Execution completed: {StatusMessage}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error during execution: {ex.Message}";
            AddLog("ERR", $"Unhandled execution failure: {ex.Message}");
        }
        finally
        {
            foreach (var card in selected)
            {
                card.IsApplying = false;
            }
            IsBusy = false;
            ExecutionProgress = 100;
        }
    }

    [RelayCommand]
    public async Task UndoSelectedTweaksAsync()
    {
        var selected = _allTweaks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No tweaks selected to undo.";
            AddLog("WARN", "Undo requested but no tweaks were selected.");
            return;
        }

        if (!IsDryRun && !AppAdminGuard.EnsureAdmin("undo system tweaks"))
        {
            StatusMessage = "Administrator privileges required to undo tweaks.";
            AddLog("ERR", "Cannot undo tweaks: Administrator privileges are required.");
            return;
        }

        IsLogDrawerOpen = true;
        IsBusy = true;
        ExecutionProgress = 0;

        foreach (var card in selected)
        {
            card.IsApplying = true;
        }

        await Task.Yield();

        try
        {
            if (IsDryRun)
            {
                AddLog("INFO", $"--- DRY-RUN MODE: Simulating undo for {selected.Count} tweak(s) ---");
                for (int i = 0; i < selected.Count; i++)
                {
                    var tweak = selected[i];
                    AddLog("SKIP", $"[DRY-RUN] Would revert: {tweak.Title} ({tweak.Id})");
                    ExecutionProgress = ((i + 1) / (double)selected.Count) * 100;
                }
                StatusMessage = $"Dry-run undo completed for {selected.Count} tweak(s).";
                AddLog("OK", "Dry-run undo simulation completed successfully.");
                return;
            }

            AddLog("INFO", $"Reverting {selected.Count} tweak(s)...");

            var tweakDefs = selected.Select(c => c.Tweak).ToList();
            var report = await Task.Run(async () => await _tweakService.UndoTweaksAsync(tweakDefs, _runner));

            foreach (var item in report.Results)
            {
                string level = item.Status == OperationItemStatus.Succeeded ? "OK" : "ERR";
                AddLog(level, $"Revert {item.Name}: {item.Reason}");
            }

            foreach (var card in selected)
            {
                card.IsActive = false;
            }

            UpdateCounts();
            StatusMessage = $"Reverted {report.SucceededCount} tweak(s), {report.FailedCount} failed.";
            AddLog("OK", $"Undo completed: {StatusMessage}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error during revert: {ex.Message}";
            AddLog("ERR", $"Unhandled undo failure: {ex.Message}");
        }
        finally
        {
            foreach (var card in selected)
            {
                card.IsApplying = false;
            }
            IsBusy = false;
            ExecutionProgress = 100;
        }
    }

    [RelayCommand]
    public async Task RescanSystemAsync()
    {
        AddLog("INFO", "Initiating manual system re-scan...");
        await Task.Run(() => ScanSystemStateAsync());
    }

    [RelayCommand]
    public async Task CreateRestorePointAsync()
    {
        if (!AppAdminGuard.EnsureAdmin("create a system restore point"))
        {
            StatusMessage = "Administrator privileges required to create a System Restore point.";
            AddLog("ERR", "Cannot create restore point: Administrator privileges required.");
            return;
        }

        IsLogDrawerOpen = true;
        AddLog("INFO", "Requesting Windows System Restore Point creation...");
        await Task.Yield();

        try
        {
            using var activity = _activityTracker?.BeginActivity("Creating System Restore Point...");
            var res = await Task.Run(async () => await _tweakService.BackupManager.CreateSystemRestorePointAsync($"WinVelo Manual Restore Point {DateTime.Now:yyyy-MM-dd HH:mm}"));
            if (res.Success)
            {
                AddLog("OK", "System Restore Point created successfully.");
                StatusMessage = "System Restore Point created successfully.";
            }
            else
            {
                string msg = res.WasThrottledOrSkipped
                    ? "Windows restricted creation (24-hour rate limit). Registry backup snapshot was preserved."
                    : (res.ErrorMessage ?? "Could not create restore point (check if System Protection is enabled).");
                AddLog("WARN", msg);
                StatusMessage = msg;
            }
        }
        catch (Exception ex)
        {
            AddLog("ERR", $"Restore point error: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ToggleLogDrawer()
    {
        IsLogDrawerOpen = !IsLogDrawerOpen;
    }

    [RelayCommand]
    public void ClearLogs()
    {
        LogEntries.Clear();
    }

    [RelayCommand]
    public async Task OpenDiffPreviewAsync()
    {
        var selected = _allTweaks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No tweaks currently selected to preview.";
            return;
        }

        await Task.Yield();
        var tweakDefs = selected.Select(c => c.Tweak).ToList();
        PreviewReportText = await Task.Run(async () => await _tweakService.GenerateDryRunDiffAsync(tweakDefs));
        IsPreviewDialogOpen = true;
    }

    [RelayCommand]
    public void CloseDiffPreview()
    {
        IsPreviewDialogOpen = false;
    }

    private void AddLog(string level, string message)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            LogEntries.Insert(0, new LogEntryItem
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                Level = level,
                Message = message
            });

            while (LogEntries.Count > 250)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
            }
        });
    }

    // Methods for test or external callers
    public List<string> GetSelectedTweakIds() => _allTweaks.Where(t => t.IsSelected).Select(t => t.Id).ToList();

    public void SetSelectedTweakIds(IEnumerable<string> ids)
    {
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        foreach (var card in _allTweaks)
        {
            card.IsSelected = idSet.Contains(card.Id) || (card.Origin != null && idSet.Contains(card.Origin));
        }
        UpdateCounts();
    }
}

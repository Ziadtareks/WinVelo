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
using Tweakora.Core.Utilities;
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
    public string Evidence => Tweak.Evidence ?? "documented";
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
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(ToggleCheckedState))]
    private TweakState _state = TweakState.NotApplied;

    public bool IsActive
    {
        get => State == TweakState.Applied;
        set => State = value ? TweakState.Applied : TweakState.NotApplied;
    }

    public bool? ToggleCheckedState => State switch
    {
        TweakState.Applied => true,
        TweakState.Partial => null,
        TweakState.Unknown => false,
        _ => false
    };

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

    public string StatusBadgeText
    {
        get
        {
            if (IsApplying) return "Applying...";
            return State switch
            {
                TweakState.Applied => "Applied",
                TweakState.Partial => "Partial",
                TweakState.Unknown => "Can't verify",
                _ => "Not applied"
            };
        }
    }

    public string StatusBadgeBackground
    {
        get
        {
            if (IsApplying) return "#1E3A8A";
            return State switch
            {
                TweakState.Applied => "#064E3B",
                TweakState.Partial => "#451A03",
                TweakState.Unknown => "#1E222D",
                _ => "#1E222D"
            };
        }
    }

    public string StatusBadgeForeground
    {
        get
        {
            if (IsApplying) return "#60A5FA";
            return State switch
            {
                TweakState.Applied => "#10B981",
                TweakState.Partial => "#F59E0B",
                TweakState.Unknown => "#94A3B8",
                _ => "#94A3B8"
            };
        }
    }

    public string StatusBadgeBorder
    {
        get
        {
            if (IsApplying) return "#3B82F6";
            return State switch
            {
                TweakState.Applied => "#059669",
                TweakState.Partial => "#D97706",
                TweakState.Unknown => "#475569",
                _ => "#334155"
            };
        }
    }

    public string RiskBadgeBackground => Risk switch
    {
        "Safe" => "#064E3B",
        "Low" => "#1E3A8A",
        "Moderate" or "Medium" => "#451A03",
        "Caution" or "High" => "#450A0A",
        _ => "#1E222D"
    };

    public string RiskBadgeForeground => Risk switch
    {
        "Safe" => "#10B981",
        "Low" => "#60A5FA",
        "Moderate" or "Medium" => "#F59E0B",
        "Caution" or "High" => "#EF4444",
        _ => "#94A3B8"
    };

    public string RiskBadgeBorder => Risk switch
    {
        "Safe" => "#059669",
        "Low" => "#3B82F6",
        "Moderate" or "Medium" => "#D97706",
        "Caution" or "High" => "#DC2626",
        _ => "#334155"
    };

    public string WhatItDoes => !string.IsNullOrWhiteSpace(Tweak.WhatItDoes) ? Tweak.WhatItDoes : Tweak.Description;

    public string SideEffects => !string.IsNullOrWhiteSpace(Tweak.SideEffects)
        ? Tweak.SideEffects
        : (string.Equals(Risk, "Safe", StringComparison.OrdinalIgnoreCase) ? "None known." : "See documentation for potential impacts.");

    public IReadOnlyList<string> Sources
    {
        get
        {
            if (Tweak.Sources != null && Tweak.Sources.Count > 0)
                return Tweak.Sources;

            if (Tweak.Source.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(Tweak.Source.GetString()))
                return new List<string> { Tweak.Source.GetString()! };

            return Array.Empty<string>();
        }
    }

    public bool HasSources => Sources.Count > 0;
    public string? SourceQuote => Tweak.SourceQuote;
    public bool HasSourceQuote => !string.IsNullOrWhiteSpace(SourceQuote);
    public string? UiEquivalentLocation => Tweak.UiEquivalentLocation;
    public bool HasUiEquivalentLocation => !string.IsNullOrWhiteSpace(UiEquivalentLocation);
    public bool IsUiEquivalent => string.Equals(Tweak.Evidence, "ui-equivalent", StringComparison.OrdinalIgnoreCase);
    public bool NeedsReview => Tweak.NeedsReview;

    public IReadOnlyList<string> ExactChanges => TweakActionFormatter.FormatExactChanges(Tweak);
    public bool HasExactChanges => ExactChanges.Count > 0;

    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    public void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    public void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

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
    private readonly IActivityTracker? _activityTracker;
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
    private bool _hasFailedDetails;

    [ObservableProperty]
    private bool _isDetailsDialogOpen;

    [ObservableProperty]
    private string _detailsDialogTitle = "Execution Details";

    public ObservableCollection<OperationItemResult> ExecutionDetailsItems { get; } = new();

    [RelayCommand]
    public void ShowDetailsDialog()
    {
        if (ExecutionDetailsItems.Count > 0)
        {
            IsDetailsDialogOpen = true;
        }
    }

    [RelayCommand]
    public void CloseDetailsDialog()
    {
        IsDetailsDialogOpen = false;
    }

    [ObservableProperty]
    private bool _isIrreversibleDialogOpen;

    [ObservableProperty]
    private string _irreversibleDialogTitle = "Irreversible Action Warning";

    public ObservableCollection<IrreversibleItemModel> IrreversibleWarningItems { get; } = new();

    private TaskCompletionSource<bool>? _irreversibleConfirmTcs;

    [RelayCommand]
    public void ConfirmIrreversible()
    {
        IsIrreversibleDialogOpen = false;
        _irreversibleConfirmTcs?.TrySetResult(true);
    }

    [RelayCommand]
    public void CancelIrreversible()
    {
        IsIrreversibleDialogOpen = false;
        _irreversibleConfirmTcs?.TrySetResult(false);
    }

    public async Task<bool> RequestIrreversibleConfirmationAsync(IEnumerable<TweakDefinition> tweaks)
    {
        var irreversible = tweaks.Where(t => !t.Reversible).ToList();
        if (irreversible.Count == 0) return true;

        IrreversibleWarningItems.Clear();
        foreach (var t in irreversible)
        {
            IrreversibleWarningItems.Add(new IrreversibleItemModel
            {
                Id = t.Id,
                Name = t.Name,
                Reason = t.IrreversibleReason ?? "Permanent system modification."
            });
        }

        IrreversibleDialogTitle = irreversible.Count == 1
            ? $"Irreversible Action — {irreversible[0].Name}"
            : $"Irreversible Actions Warning ({irreversible.Count} tweaks)";

        _irreversibleConfirmTcs = new TaskCompletionSource<bool>();
        IsIrreversibleDialogOpen = true;

        return await _irreversibleConfirmTcs.Task;
    }

    [ObservableProperty]
    private bool _isPreviewDialogOpen;

    [ObservableProperty]
    private string _previewReportText = string.Empty;

    public bool IsAdmin => AdminPrivilegeHelper.IsRunningAsAdministrator();

    private readonly UnifiedExecutionDialogViewModel? _executionDialog;

    public TweaksViewModel(
        ITweakService tweakService,
        IOperationRunner runner,
        IActivityTracker? activityTracker = null,
        UnifiedExecutionDialogViewModel? executionDialog = null)
    {
        _tweakService = tweakService;
        _runner = runner;
        _activityTracker = activityTracker;
        _executionDialog = executionDialog;

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
                    var state = await _tweakService.DetectTweakStateAsync(card.Tweak, cancellationToken);
                    if (card.State != state)
                    {
                        Application.Current?.Dispatcher?.InvokeAsync(() =>
                        {
                            card.State = state;
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
    private bool _showExperimentalTweaks = false;

    [ObservableProperty]
    private bool _isRecommendedOnly = false;

    [ObservableProperty]
    private string _selectedRisk = "All";

    [ObservableProperty]
    private string _selectedState = "All";

    public IReadOnlyList<string> RiskOptions { get; } = new[] { "All", "Safe", "Low", "Medium", "High" };
    public IReadOnlyList<string> StateOptions { get; } = new[] { "All", "Applied", "Partial", "Not applied", "Can't verify" };
    public IReadOnlyList<string> CategoryOptions { get; } = new[] { "All", "Essential", "Privacy", "Performance", "Customize UI", "Advanced" };

    // Toast notification properties
    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private string _toastActionText = "Undo";

    private TweakCardModel? _lastAppliedToastCard;
    private System.Windows.Threading.DispatcherTimer? _toastDismissTimer;

    public void SetSearchQuery(string query)
    {
        SearchQuery = query;
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnSelectedRiskChanged(string value) => ApplyFilter();
    partial void OnSelectedStateChanged(string value) => ApplyFilter();
    partial void OnShowExperimentalTweaksChanged(bool value) => ApplyFilter();
    partial void OnIsRecommendedOnlyChanged(bool value) => ApplyFilter();

    [RelayCommand]
    public void ToggleRecommendedOnly()
    {
        IsRecommendedOnly = !IsRecommendedOnly;
    }

    [RelayCommand]
    public void SetMode(string mode)
    {
        // Simple = only Recommended tweaks are visible. Advanced = everything; Experimental stays behind its own toggle.
        IsRecommendedOnly = string.Equals(mode, "Simple", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(mode, "Beginner", StringComparison.OrdinalIgnoreCase);
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
            // Experimental filter: hide unproven community tweaks unless explicitly enabled
            bool isExperimental = string.Equals(card.Category, "Experimental", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(card.Tweak.Evidence, "anecdotal", StringComparison.OrdinalIgnoreCase);
            if (isExperimental && !ShowExperimentalTweaks)
            {
                continue;
            }

            // Recommended only filter: Safe or Low, evidence documented or ui-equivalent, reversible, not needsReview, not Experimental
            if (IsRecommendedOnly)
            {
                bool isRecommended = (string.Equals(card.Risk, "Safe", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(card.Risk, "Low", StringComparison.OrdinalIgnoreCase)) &&
                                     (string.Equals(card.Evidence, "documented", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(card.Evidence, "ui-equivalent", StringComparison.OrdinalIgnoreCase)) &&
                                     card.Tweak.Reversible &&
                                     !card.NeedsReview &&
                                     !string.Equals(card.Category, "Experimental", StringComparison.OrdinalIgnoreCase);
                if (!isRecommended)
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

            // Risk filter
            if (!string.Equals(SelectedRisk, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(card.Risk, SelectedRisk, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            // State filter
            if (!string.Equals(SelectedState, "All", StringComparison.OrdinalIgnoreCase))
            {
                bool stateMatches = SelectedState switch
                {
                    "Applied" => card.State == TweakState.Applied,
                    "Partial" => card.State == TweakState.Partial,
                    "Not applied" => card.State == TweakState.NotApplied,
                    "Can't verify" => card.State == TweakState.Unknown,
                    _ => true
                };
                if (!stateMatches) continue;
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
        if (card == null || card.IsBusy || card.IsApplying || IsScanning) return;
        bool targetState = !card.IsActive;

        bool isMediumOrHigh = string.Equals(card.Risk, "High", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(card.Risk, "Caution", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(card.Risk, "Medium", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(card.Risk, "Moderate", StringComparison.OrdinalIgnoreCase);

        // Medium and High tweaks never apply immediately; they open the shared preview dialog
        if (isMediumOrHigh)
        {
            if (_executionDialog != null)
            {
                string verb = targetState ? "Apply" : "Revert";
                await _executionDialog.OpenPreviewAsync(
                    title: $"{verb} Tweak — {card.Title}",
                    description: $"Review the system impact of {card.Title} before applying.",
                    notice: "Medium and High risk modifications require explicit review and confirmation before execution.",
                    confirmText: $"{verb} Tweak",
                    tweaks: new[] { card.Tweak },
                    isUndo: !targetState,
                    onConfirmed: async (selected) =>
                    {
                        card.IsBusy = true;
                        card.IsApplying = true;
                        try
                        {
                            OperationExecutionReport rep;
                            if (targetState)
                            {
                                rep = await _tweakService.ApplyTweaksAsync(selected, _runner);
                            }
                            else
                            {
                                rep = await _tweakService.UndoTweaksAsync(selected, _runner);
                            }
                            _ = WindowsSettingNotifier.NotifySettingsChangedAsync();
                            var actualState = await _tweakService.DetectTweakStateAsync(card.Tweak);
                            card.State = actualState;
                            UpdateCounts();

                            ServicingHealthReport? health = null;
                            try { health = await _tweakService.CheckServicingHealthAsync(); } catch { }
                            _executionDialog.ShowResult(selected, rep, health, isUndo: !targetState);
                        }
                        finally
                        {
                            card.IsApplying = false;
                            card.IsBusy = false;
                        }
                    });
                return;
            }
        }

        // Safe and Low: apply immediately and show toast with Undo
        if (targetState && !card.Tweak.Reversible)
        {
            bool confirmed = await RequestIrreversibleConfirmationAsync(new[] { card.Tweak });
            if (!confirmed)
            {
                AddLog("INFO", $"Cancelled application of irreversible tweak: {card.Title}");
                return;
            }
        }

        card.IsBusy = true;
        card.IsApplying = true;

        string actionVerb = targetState ? "Applying" : "Reverting";
        using var activity = _activityTracker?.BeginActivity($"{actionVerb} {card.Title}...");
        await Task.Yield();

        try
        {
            var (report, actualState) = await Task.Run(async () =>
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
                TweakState actualState = await _tweakService.DetectTweakStateAsync(card.Tweak);
                return (rep, actualState);
            });

            card.State = actualState;
            UpdateCounts();

            var res = report.Results.FirstOrDefault();
            bool opSucceeded = res != null && (res.Status is OperationItemStatus.Succeeded or OperationItemStatus.Applied or OperationItemStatus.Restored);
            if (opSucceeded)
            {
                AddLog("OK", $"Successfully {(actualState == TweakState.Applied ? "applied" : "reverted")}: {card.Title}");
                ShowApplyToast(card, actualState == TweakState.Applied ? "Applied — Undo" : "Reverted — Undo");
            }
            else
            {
                AddLog("ERR", $"Failed {card.Title}: {res?.Reason ?? "System policy updated"}");
            }
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

    [RelayCommand]
    public async Task UndoToastActionAsync()
    {
        IsToastVisible = false;
        _toastDismissTimer?.Stop();

        if (_lastAppliedToastCard == null) return;
        var card = _lastAppliedToastCard;
        _lastAppliedToastCard = null;

        card.IsBusy = true;
        card.IsApplying = true;
        using var activity = _activityTracker?.BeginActivity($"Undoing {card.Title}...");
        await Task.Yield();

        try
        {
            bool wasApplied = card.IsActive;
            if (wasApplied)
            {
                await Task.Run(() => _tweakService.UndoTweaksAsync(new[] { card.Tweak }, _runner));
            }
            else
            {
                await Task.Run(() => _tweakService.ApplyTweaksAsync(new[] { card.Tweak }, _runner));
            }
            _ = WindowsSettingNotifier.NotifySettingsChangedAsync();
            var newState = await _tweakService.DetectTweakStateAsync(card.Tweak);
            card.State = newState;
            UpdateCounts();
            AddLog("OK", $"Reverted via toast undo: {card.Title}");
        }
        catch (Exception ex)
        {
            AddLog("ERR", $"Toast undo failed for {card.Title}: {ex.Message}");
        }
        finally
        {
            card.IsApplying = false;
            card.IsBusy = false;
        }
    }

    [RelayCommand]
    public void DismissToast()
    {
        IsToastVisible = false;
        _toastDismissTimer?.Stop();
        _lastAppliedToastCard = null;
    }

    private void ShowApplyToast(TweakCardModel card, string message = "Applied — Undo")
    {
        _lastAppliedToastCard = card;
        ToastMessage = message;
        IsToastVisible = true;

        if (System.Windows.Application.Current?.Dispatcher != null)
        {
            _toastDismissTimer?.Stop();
            _toastDismissTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(8)
            };
            _toastDismissTimer.Tick += (s, e) =>
            {
                _toastDismissTimer.Stop();
                IsToastVisible = false;
                _lastAppliedToastCard = null;
            };
            _toastDismissTimer.Start();
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

        var tweakDefs = selected.Select(c => c.Tweak).ToList();
        var irreversible = tweakDefs.Where(t => !t.Reversible).ToList();
        if (!IsDryRun && irreversible.Count > 0)
        {
            bool confirmed = await RequestIrreversibleConfirmationAsync(irreversible);
            if (!confirmed)
            {
                StatusMessage = "Cancelled application of irreversible tweaks.";
                AddLog("INFO", "Cancelled application of irreversible tweaks.");
                return;
            }
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

            // Update state: ONLY set IsActive for cards that actually succeeded!
            var succeededIds = new HashSet<string>(
                report.Results.Where(r => r.Status is OperationItemStatus.Succeeded or OperationItemStatus.Applied)
                              .Select(r => r.Id),
                StringComparer.OrdinalIgnoreCase);

            foreach (var card in selected)
            {
                if (succeededIds.Contains(card.Id) || succeededIds.Contains(card.Tweak.Id) || (card.Tweak.Origin != null && succeededIds.Contains(card.Tweak.Origin)))
                {
                    card.IsActive = true;
                }
                else
                {
                    card.IsActive = false;
                }
            }

            UpdateCounts();

            if (report.FailedCount > 0)
            {
                StatusMessage = $"Applied {report.SucceededCount} of {report.TotalRequested} — {report.FailedCount} failed (details)";
                HasFailedDetails = true;
                DetailsDialogTitle = "Execution Details — Apply Tweaks";
                ExecutionDetailsItems.Clear();
                foreach (var f in report.Results.Where(r => r.IsFailure)) ExecutionDetailsItems.Add(f);
                IsLogDrawerOpen = true;
            }
            else if (report.SkippedCount > 0)
            {
                StatusMessage = $"Applied {report.SucceededCount} of {report.TotalRequested} ({report.SkippedCount} skipped).";
                HasFailedDetails = false;
                ExecutionDetailsItems.Clear();
            }
            else
            {
                StatusMessage = $"Applied all {report.SucceededCount} tweak(s) successfully.";
                HasFailedDetails = false;
                ExecutionDetailsItems.Clear();
            }
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
                string level = item.Status is OperationItemStatus.Succeeded or OperationItemStatus.Restored ? "OK" : "ERR";
                AddLog(level, $"Revert {item.Name}: {item.Reason}");
            }

            var restoredIds = new HashSet<string>(
                report.Results.Where(r => r.Status is OperationItemStatus.Succeeded or OperationItemStatus.Restored)
                              .Select(r => r.Id),
                StringComparer.OrdinalIgnoreCase);

            foreach (var card in selected)
            {
                if (restoredIds.Contains(card.Id) || restoredIds.Contains(card.Tweak.Id) || (card.Tweak.Origin != null && restoredIds.Contains(card.Tweak.Origin)))
                {
                    card.IsActive = false;
                }
            }

            UpdateCounts();

            if (report.FailedCount > 0)
            {
                StatusMessage = $"Restored {report.SucceededCount} of {report.TotalRequested} — {report.FailedCount} failed (details)";
                HasFailedDetails = true;
                DetailsDialogTitle = "Execution Details — Revert Tweaks";
                ExecutionDetailsItems.Clear();
                foreach (var f in report.Results.Where(r => r.IsFailure)) ExecutionDetailsItems.Add(f);
                IsLogDrawerOpen = true;
            }
            else
            {
                StatusMessage = $"Restored all {report.SucceededCount} tweak(s) successfully.";
                HasFailedDetails = false;
                ExecutionDetailsItems.Clear();
            }
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

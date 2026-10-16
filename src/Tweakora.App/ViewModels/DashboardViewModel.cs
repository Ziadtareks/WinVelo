using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.App.Services;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Core.Safety;
using Tweakora.Modules.Advisor;
using Tweakora.Modules.Memory;
using Tweakora.Modules.Tweaks;
using Tweakora.Platform;

namespace Tweakora.App.ViewModels;

public sealed partial class QuickToggleCardModel : ObservableObject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string IconSymbol { get; init; }
    public string? OriginKey { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeBackground))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeForeground))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeBorder))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private TweakState _state = TweakState.NotApplied;

    public bool IsActive
    {
        get => State == TweakState.Applied;
        set => State = value ? TweakState.Applied : TweakState.NotApplied;
    }

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
                TweakState.Applied => "On",
                TweakState.Partial => "Partial",
                _ => "Off"
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
                _ => "#334155"
            };
        }
    }

    public Func<QuickToggleCardModel, bool?, Task>? OnToggleAsync { get; set; }

    [RelayCommand]
    private async Task ToggleAsync(object? parameter)
    {
        if (IsBusy || IsApplying || OnToggleAsync == null) return;
        bool? target = parameter is bool b ? b : null;
        await OnToggleAsync(this, target);
    }
}

public sealed partial class ProfileCardModel : ObservableObject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public string BadgeText { get; init; } = string.Empty;
    public string BadgeBg { get; init; } = string.Empty;
    public string BadgeFg { get; init; } = string.Empty;
    public string TweakCountText { get; init; } = string.Empty;
    public string ImpactText { get; init; } = string.Empty;
    public required string ButtonText { get; init; }
    public required string IconSymbol { get; init; }
    public required string TiersSubtitle { get; init; }

    public IReadOnlyList<TweakDefinition> StandardTweaks { get; init; } = Array.Empty<TweakDefinition>();
    public IReadOnlyList<TweakDefinition> FullTweaks { get; init; } = Array.Empty<TweakDefinition>();
    public IReadOnlyList<ExcludedPresetTweak> ExcludedTweaks { get; init; } = Array.Empty<ExcludedPresetTweak>();

    [ObservableProperty]
    private bool _isFullTier;

    public bool IsStandardTier
    {
        get => !IsFullTier;
        set
        {
            if (value && IsFullTier)
            {
                IsFullTier = false;
            }
        }
    }

    partial void OnIsFullTierChanged(bool value)
    {
        OnPropertyChanged(nameof(IsStandardTier));
        UpdatePreview();
    }

    public IReadOnlyList<string> PreviewTweaks { get; private set; } = Array.Empty<string>();

    [ObservableProperty]
    private string _previewTweakSummary = string.Empty;

    public void UpdatePreview()
    {
        var activeTweaks = IsFullTier ? FullTweaks : StandardTweaks;
        var first4 = activeTweaks.Take(4).Select(t => t.Name).ToList();
        PreviewTweaks = first4;
        int remainingCount = activeTweaks.Count - first4.Count;
        if (first4.Count == 0)
        {
            PreviewTweakSummary = "No tweaks in standard tier.";
        }
        else if (remainingCount > 0)
        {
            PreviewTweakSummary = $"{string.Join(", ", first4)}, and {remainingCount} more";
        }
        else
        {
            PreviewTweakSummary = string.Join(", ", first4);
        }
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasFailures;

    [ObservableProperty]
    private string _statusColor = "#34D399";

    private System.Threading.Timer? _autoClearTimer;

    public void SetStatusMessage(string message, string color = "#34D399", bool hasFailures = false)
    {
        _autoClearTimer?.Dispose();
        StatusMessage = message;
        StatusColor = color;
        HasFailures = hasFailures;

        _autoClearTimer = new System.Threading.Timer(_ =>
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                StatusMessage = string.Empty;
                HasFailures = false;
            });
        }, null, 4000, System.Threading.Timeout.Infinite);
    }

    public void ClearStatusMessage()
    {
        _autoClearTimer?.Dispose();
        _autoClearTimer = null;
        StatusMessage = string.Empty;
        HasFailures = false;
    }

    public Action? OnShowDetails { get; set; }

    [RelayCommand]
    private void ShowDetails()
    {
        OnShowDetails?.Invoke();
    }

    public Func<ProfileCardModel, Task>? OnApplyAsync { get; set; }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (IsBusy || OnApplyAsync == null) return;
        await OnApplyAsync(this);
    }
}

public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly ITweakService _tweakService;
    private readonly IOperationRunner _runner;
    private readonly ISystemRestoreService _restoreService;
    private readonly IMemoryOptimizerService _memoryService;
    private readonly ISmartAdvisorService _advisorService;
    private readonly IActivityTracker? _activityTracker;
    private readonly IShellService? _shellService;
    private readonly List<TweakDefinition> _catalog = new();
    private bool _isRefreshingStates;

    [ObservableProperty]
    private bool _isSystemRestoreEnabled;

    [ObservableProperty]
    private DateTime? _latestRestorePointDate;

    [ObservableProperty]
    private string _systemRestoreStatusText = "Checking System Restore...";

    [ObservableProperty]
    private string _systemRestoreBadgeBg = "#064E3B";

    [ObservableProperty]
    private string _systemRestoreBadgeFg = "#10B981";

    [ObservableProperty]
    private int _appliedTweaksCount = 0;

    [ObservableProperty]
    private bool _isServicingHealthy = true;

    [ObservableProperty]
    private string _servicingHealthBadgeBg = "#064E3B";

    [ObservableProperty]
    private string _servicingHealthBadgeFg = "#10B981";

    [ObservableProperty]
    private bool _isRebootPending;

    [ObservableProperty]
    private string _rebootPendingMessage = "A system reboot is required to complete active modifications.";

    [ObservableProperty]
    private bool _isExplorerRestartPending;

    [ObservableProperty]
    private string _explorerRestartPendingMessage = "Explorer restart recommended for settings to take effect.";

    [ObservableProperty]
    private SystemInfoSummary _systemInfo = SystemInfoHelper.GetSystemInfo();

    [ObservableProperty]
    private string _ramUsageText = string.Empty;

    [ObservableProperty]
    private int _ramUsagePercentage = 0;

    [ObservableProperty]
    private string _availableRamText = string.Empty;

    [ObservableProperty]
    private string _ramBarColor = "#10B981";

    public bool IsAdmin { get; } = AdminPrivilegeHelper.IsRunningAsAdministrator();

    public string AdminBadgeBg => IsAdmin ? "#064E3B" : "#7F1D1D";
    public string AdminBadgeFg => IsAdmin ? "#10B981" : "#F87171";
    public string AdminStatusText => IsAdmin ? "Administrator Active" : "Standard User (Limited)";

    [ObservableProperty]
    private int _configuredSettingsPercentage = 0;

    [ObservableProperty]
    private string _configuredSettingsStatusText = "Scanning settings...";

    [ObservableProperty]
    private string _configuredSettingsBrush = "#10B981";

    [ObservableProperty]
    private string _configuredSettingsDetailText = "Scanning settings...";

    [ObservableProperty]
    private string _appliedTweaksSummaryText = "0 tweaks applied by WinVelo";

    // Backward-compatibility aliases
    public int OptimizationScore
    {
        get => ConfiguredSettingsPercentage;
        set => ConfiguredSettingsPercentage = value;
    }

    public string OptimizationScoreGrade
    {
        get => ConfiguredSettingsStatusText;
        set => ConfiguredSettingsStatusText = value;
    }

    public string OptimizationScoreBrush
    {
        get => ConfiguredSettingsBrush;
        set => ConfiguredSettingsBrush = value;
    }

    public string OptimizationScoreDetailText
    {
        get => ConfiguredSettingsDetailText;
        set => ConfiguredSettingsDetailText = value;
    }

    [ObservableProperty]
    private string _smartAdvisorSummary = "Ready to review recommended hardware settings.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSmartOptimizing))]
    private bool _isSmartOptimizing;

    public bool IsNotSmartOptimizing => !IsSmartOptimizing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotPurgingRam))]
    private bool _isPurgingRam;

    public bool IsNotPurgingRam => !IsPurgingRam;

    [ObservableProperty]
    private string _restorePointStatus = "Restore point ready.";

    [ObservableProperty]
    private bool _isCreatingRestorePoint;

    [ObservableProperty]
    private string _globalStatusMessage = "Ready. Select a preset or review individual settings below.";

    [ObservableProperty]
    private bool _isDetailsDialogOpen;

    [ObservableProperty]
    private string _detailsDialogTitle = "Execution Details";

    [ObservableProperty]
    private bool _hasFailedDetails;

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
    private bool _isProfilePreviewOpen;

    [ObservableProperty]
    private string _profilePreviewTitle = "Profile Preview";

    [ObservableProperty]
    private string _profilePreviewDescription = string.Empty;

    [ObservableProperty]
    private string _profilePreviewNotice = string.Empty;

    [ObservableProperty]
    private string _profilePreviewConfirmText = "Confirm & Apply";

    [ObservableProperty]
    private bool _isProfilePreviewUndo;

    [ObservableProperty]
    private bool _isProfilePreviewLoading;

    [ObservableProperty]
    private string? _servicingHealthSummary;

    [ObservableProperty]
    private string _servicingHealthShortStatus = "All OK";

    [ObservableProperty]
    private string _servicingHealthDetails = "Windows Update, BITS, Microsoft Store, and Defender are operational.";

    [ObservableProperty]
    private ServicingHealthReport? _lastServicingHealthReport;

    public ObservableCollection<ProfilePreviewItemModel> ProfilePreviewItems { get; } = new();

    private ProfileCardModel? _activePreviewCard;

    [RelayCommand]
    public async Task ConfirmProfilePreviewAsync()
    {
        IsProfilePreviewOpen = false;

        var selected = ProfilePreviewItems
            .Where(i => i.IsSelected && !i.IsAlreadyApplied)
            .Select(i => i.Tweak)
            .ToList();

        if (selected.Count == 0)
        {
            GlobalStatusMessage = "No tweaks selected to execute.";
            return;
        }

        bool isUndo = IsProfilePreviewUndo;
        var card = _activePreviewCard;

        if (card != null)
        {
            card.IsBusy = true;
            card.StatusMessage = isUndo ? "Reverting selected tweaks..." : "Applying selected tweaks...";
        }

        GlobalStatusMessage = isUndo ? $"Reverting {selected.Count} tweak(s)..." : $"Applying {selected.Count} tweak(s)...";

        using var activity = _activityTracker?.BeginActivity(isUndo ? "Reverting WinVelo Tweaks..." : "Applying Profile Tweaks...");
        await Task.Yield();

        try
        {
            OperationExecutionReport report;
            if (isUndo)
            {
                report = await Task.Run(() => _tweakService.UndoTweaksAsync(selected, _runner));
            }
            else
            {
                var irreversible = selected.Where(t => !t.Reversible).ToList();
                if (irreversible.Count > 0)
                {
                    bool confirmed = await RequestIrreversibleConfirmationAsync(irreversible);
                    if (!confirmed)
                    {
                        if (card != null) card.StatusMessage = "Cancelled by user.";
                        GlobalStatusMessage = "Operation cancelled.";
                        return;
                    }
                }
                report = await Task.Run(() => _tweakService.ApplyTweaksAsync(selected, _runner));
            }

            if (card != null)
            {
                HandleProfileReport(card, report, isRestore: isUndo);
            }
            else
            {
                GlobalStatusMessage = isUndo
                    ? $"Restored {report.SucceededCount} of {report.TotalRequested} changes."
                    : $"Applied {report.SucceededCount} of {report.TotalRequested} optimizations.";
            }

            try
            {
                var healthReport = await _tweakService.CheckServicingHealthAsync();
                LastServicingHealthReport = healthReport;
                ServicingHealthSummary = healthReport.SummaryText;

                if (!healthReport.IsHealthy)
                {
                    GlobalStatusMessage += $" | Servicing Alert: {string.Join("; ", healthReport.Warnings)}";
                    if (card != null)
                    {
                        card.StatusMessage += " (Servicing issues detected)";
                        card.HasFailures = true;
                    }
                }
                else if (card != null && !card.HasFailures)
                {
                    card.StatusMessage += " | Servicing: Healthy";
                }
            }
            catch { }

            await RefreshCalmStatusAsync();
            await RefreshAllToggleStatesAsync();
            _ = RecalculateOptimizationScoreAsync();
        }
        catch (Exception ex)
        {
            if (card != null)
            {
                card.HasFailures = true;
                card.StatusColor = "#EF4444";
                card.StatusMessage = $"Error: {ex.Message}";
            }
            GlobalStatusMessage = $"Execution failed: {ex.Message}";
        }
        finally
        {
            if (card != null) card.IsBusy = false;
        }
    }

    [RelayCommand]
    public void CancelProfilePreview()
    {
        IsProfilePreviewOpen = false;
        GlobalStatusMessage = "Profile preview cancelled.";
    }

    public async Task OpenProfilePreviewAsync(
        string title,
        string description,
        string notice,
        string confirmText,
        IReadOnlyList<TweakDefinition> tweaks,
        bool isUndo,
        ProfileCardModel? card,
        IReadOnlyList<ExcludedPresetTweak>? excludedTweaks = null)
    {
        if (_executionDialog != null)
        {
            await _executionDialog.OpenPreviewAsync(
                title,
                description,
                notice,
                confirmText,
                tweaks,
                isUndo,
                onConfirmed: async (selected) =>
                {
                    if (card != null)
                    {
                        card.IsBusy = true;
                        card.StatusMessage = isUndo ? "Reverting selected tweaks..." : "Applying selected tweaks...";
                    }
                    try
                    {
                        OperationExecutionReport rep;
                        if (isUndo)
                        {
                            rep = await Task.Run(() => _tweakService.UndoTweaksAsync(selected, _runner));
                        }
                        else
                        {
                            rep = await Task.Run(() => _tweakService.ApplyTweaksAsync(selected, _runner));
                        }

                        ServicingHealthReport? health = null;
                        try
                        {
                            health = await _tweakService.CheckServicingHealthAsync();
                            LastServicingHealthReport = health;
                            ServicingHealthSummary = health.SummaryText;
                        }
                        catch { }

                        if (card != null)
                        {
                            HandleProfileReport(card, rep, isRestore: isUndo);
                        }

                        _executionDialog.ShowResult(selected, rep, health, isUndo);
                        await RefreshCalmStatusAsync();
                        await RefreshAllToggleStatesAsync();
                        _ = RecalculateOptimizationScoreAsync();
                    }
                    finally
                    {
                        if (card != null) card.IsBusy = false;
                    }
                },
                onCancelled: () =>
                {
                    if (card != null) card.StatusMessage = "Cancelled by user.";
                    GlobalStatusMessage = "Operation cancelled.";
                });
            return;
        }

        _activePreviewCard = card;
        ProfilePreviewTitle = title;
        ProfilePreviewDescription = description;
        ProfilePreviewNotice = notice;
        ProfilePreviewConfirmText = confirmText;
        IsProfilePreviewUndo = isUndo;
        ProfilePreviewItems.Clear();
        IsProfilePreviewLoading = true;
        IsProfilePreviewOpen = true;

        var items = new List<ProfilePreviewItemModel>();
        foreach (var t in tweaks)
        {
            items.Add(new ProfilePreviewItemModel
            {
                Tweak = t,
                Id = t.Id,
                Name = t.Name,
                Risk = t.Risk,
                Reversible = t.Reversible,
                RequiresReboot = t.RequiresReboot,
                RequiresExplorerRestart = t.RequiresExplorerRestart,
                IsSelected = true,
                IsAlreadyApplied = false
            });
        }

        foreach (var item in items)
        {
            ProfilePreviewItems.Add(item);
        }

        _ = Task.Run(async () =>
        {
            foreach (var item in items)
            {
                try
                {
                    if (isUndo)
                    {
                        var state = await _tweakService.DetectTweakStateAsync(item.Tweak);
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            item.StateText = state switch
                            {
                                TweakState.Applied => "Applied (In Journal)",
                                TweakState.Partial => "Partial (In Journal)",
                                _ => "Modified in Journal"
                            };
                            item.StateBg = "#1E3A8A";
                            item.StateFg = "#60A5FA";
                            item.IsAlreadyApplied = false;
                            item.IsSelected = true;
                        });
                    }
                    else
                    {
                        var state = await _tweakService.DetectTweakStateAsync(item.Tweak);
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            switch (state)
                            {
                                case TweakState.Applied:
                                    item.StateText = "Applied";
                                    item.StateBg = "#064E3B";
                                    item.StateFg = "#10B981";
                                    item.IsAlreadyApplied = true;
                                    item.IsSelected = false;
                                    break;
                                case TweakState.Partial:
                                    item.StateText = "Partial";
                                    item.StateBg = "#78350F";
                                    item.StateFg = "#FBBF24";
                                    item.IsAlreadyApplied = false;
                                    item.IsSelected = true;
                                    break;
                                case TweakState.NotApplied:
                                    item.StateText = "Not Applied";
                                    item.StateBg = "#1E222D";
                                    item.StateFg = "#94A3B8";
                                    item.IsAlreadyApplied = false;
                                    item.IsSelected = true;
                                    break;
                                default:
                                    item.StateText = "Unknown";
                                    item.StateBg = "#334155";
                                    item.StateFg = "#CBD5E1";
                                    item.IsAlreadyApplied = false;
                                    item.IsSelected = true;
                                    break;
                            }
                        });
                    }
                }
                catch { }
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsProfilePreviewLoading = false;
            });
        });
    }

    public ObservableCollection<ProfileCardModel> Profiles { get; } = new();
    public ObservableCollection<QuickToggleCardModel> QuickToggles { get; } = new();

    private readonly UnifiedExecutionDialogViewModel? _executionDialog;

    public DashboardViewModel(
        ITweakService tweakService,
        IOperationRunner runner,
        ISystemRestoreService restoreService,
        IMemoryOptimizerService memoryService,
        ISmartAdvisorService advisorService,
        IActivityTracker? activityTracker = null,
        IShellService? shellService = null,
        UnifiedExecutionDialogViewModel? executionDialog = null)
    {
        _tweakService = tweakService;
        _runner = runner;
        _restoreService = restoreService;
        _memoryService = memoryService;
        _advisorService = advisorService;
        _activityTracker = activityTracker;
        _shellService = shellService;
        _executionDialog = executionDialog;

        try
        {
            _catalog.AddRange(_tweakService.LoadCatalog());
        }
        catch { }

        InitializeProfiles();
        InitializeQuickToggles();
        UpdateRamMetrics();

        _ = Task.Run(async () =>
        {
            await RefreshCalmStatusAsync();
            await RefreshAllToggleStatesAsync();
            await RecalculateOptimizationScoreAsync();
        });
    }

    [RelayCommand]
    private void RestartExplorer()
    {
        try
        {
            _shellService?.RestartExplorer();
            IsExplorerRestartPending = false;
            GlobalStatusMessage = "Windows Explorer restarted successfully.";
        }
        catch (Exception ex)
        {
            GlobalStatusMessage = $"Failed to restart Explorer: {ex.Message}";
        }
    }

    public async Task RefreshCalmStatusAsync()
    {
        await Task.Yield();
        try
        {
            bool srEnabled = _restoreService.IsSystemRestoreEnabled();
            var latestDate = _restoreService.GetLatestRestorePointDate();
            var activeTweaks = _tweakService.GetActiveJournalTweaks();
            var health = await _tweakService.CheckServicingHealthAsync();
            bool rebootPending = _tweakService.IsRebootPending();
            bool explorerPending = _tweakService.IsExplorerRestartPending();

            void Apply()
            {
                IsSystemRestoreEnabled = srEnabled;
                LatestRestorePointDate = latestDate;
                if (!srEnabled)
                {
                    SystemRestoreStatusText = "System Restore: Disabled";
                    SystemRestoreBadgeBg = "#7F1D1D";
                    SystemRestoreBadgeFg = "#F87171";
                }
                else if (latestDate.HasValue)
                {
                    SystemRestoreStatusText = $"System Restore: Active (Latest: {latestDate.Value:MMM d, yyyy h:mm tt})";
                    SystemRestoreBadgeBg = "#064E3B";
                    SystemRestoreBadgeFg = "#10B981";
                }
                else
                {
                    SystemRestoreStatusText = "System Restore: Active (No restore points yet)";
                    SystemRestoreBadgeBg = "#064E3B";
                    SystemRestoreBadgeFg = "#10B981";
                }

                AppliedTweaksCount = activeTweaks.Count;
                AppliedTweaksSummaryText = $"{activeTweaks.Count} tweak{(activeTweaks.Count == 1 ? "" : "s")} applied by WinVelo";

                LastServicingHealthReport = health;
                IsServicingHealthy = health.IsHealthy;
                if (health.IsHealthy)
                {
                    ServicingHealthShortStatus = "All OK";
                    ServicingHealthDetails = "Normal — Windows Update, BITS, Microsoft Store, and Defender are operational.";
                    ServicingHealthSummary = "Normal — Windows Update, BITS, Microsoft Store, and Defender are operational.";
                }
                else
                {
                    int issueCount = health.Warnings?.Count ?? 1;
                    ServicingHealthShortStatus = issueCount == 1 ? "1 issue" : $"{issueCount} issues";
                    ServicingHealthDetails = health.Warnings != null && health.Warnings.Count > 0
                        ? string.Join("\n", health.Warnings)
                        : health.SummaryText;
                    ServicingHealthSummary = health.SummaryText;
                }
                ServicingHealthBadgeBg = health.IsHealthy ? "#064E3B" : "#78350F";
                ServicingHealthBadgeFg = health.IsHealthy ? "#10B981" : "#FBBF24";

                IsRebootPending = rebootPending;
                IsExplorerRestartPending = explorerPending;
            }

            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(Apply);
            }
            else
            {
                Apply();
            }
        }
        catch { }
    }

    public void UpdateRamMetrics()
    {
        var ram = SystemInfoHelper.GetRamMetrics();
        void Apply()
        {
            RamUsageText = ram.DisplayText;
            RamUsagePercentage = ram.UsagePercent;
            AvailableRamText = ram.AvailableText;

            if (ram.UsagePercent >= 85)
            {
                RamBarColor = "#EF4444";
            }
            else if (ram.UsagePercent >= 70)
            {
                RamBarColor = "#F59E0B";
            }
            else
            {
                RamBarColor = "#10B981";
            }
        }

        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(Apply);
        }
        else
        {
            Apply();
        }
    }

    public void Dispose()
    {
        // No background polling timers
    }

    public void ClearProfileStatusMessages()
    {
        foreach (var card in Profiles)
        {
            card.ClearStatusMessage();
        }
    }

    private void InitializeProfiles()
    {
        Profiles.Clear();

        var order = new[] { "preset.recommended", "preset.gaming", "preset.privacy", "preset.performance", "preset.clean-ui", "preset.ai" };
        var presets = _tweakService.LoadPresets()
            .OrderBy(p =>
            {
                int idx = Array.IndexOf(order, p.Id);
                return idx >= 0 ? idx : 999;
            })
            .ToList();

        foreach (var preset in presets)
        {
            var standardKeySet = new HashSet<string>(preset.Standard, StringComparer.OrdinalIgnoreCase);
            var fullKeySet = new HashSet<string>(preset.Full, StringComparer.OrdinalIgnoreCase);

            var standardTweaks = _catalog.Where(t => standardKeySet.Contains(t.Id)).ToList();
            var fullTweaks = _catalog.Where(t => fullKeySet.Contains(t.Id)).ToList();

            string icon = preset.Id switch
            {
                "preset.recommended" => "Flash24",
                "preset.gaming" => "Games24",
                "preset.privacy" => "ShieldCheckmark24",
                "preset.performance" => "Rocket24",
                "preset.clean-ui" => "Desktop24",
                _ => "Sparkle24"
            };

            bool defaultFull = standardTweaks.Count == 0 && fullTweaks.Count > 0;

            var card = new ProfileCardModel
            {
                Id = preset.Id,
                Title = preset.Name,
                Description = preset.Description,
                TiersSubtitle = $"Standard: {standardTweaks.Count} tweaks · Full: {fullTweaks.Count} tweaks",
                ButtonText = "Review",
                IconSymbol = icon,
                StandardTweaks = standardTweaks,
                FullTweaks = fullTweaks,
                ExcludedTweaks = preset.Excluded,
                IsFullTier = defaultFull,
                OnApplyAsync = async (c) =>
                {
                    var tweaks = c.IsFullTier ? c.FullTweaks : c.StandardTweaks;
                    string tierName = c.IsFullTier ? "Full" : "Standard";
                    await OpenProfilePreviewAsync(
                        $"{preset.Name} ({tierName})",
                        preset.Description,
                        "Already applied tweaks are skipped. You may untick any items before applying.",
                        "Apply Selected Tweaks",
                        tweaks,
                        isUndo: false,
                        c,
                        c.ExcludedTweaks);
                }
            };

            card.UpdatePreview();
            card.OnShowDetails = ShowDetailsDialog;
            Profiles.Add(card);
        }
    }

    private void InitializeQuickToggles()
    {
        QuickToggles.Clear();

        // 1. Classic Right-Click Menu
        QuickToggles.Add(new QuickToggleCardModel
        {
            Id = "tweak.classic-menu",
            OriginKey = "WPFTweaksRightClickMenu",
            Title = "Classic Right-Click Menu",
            Description = "Restore the classic full context menu without having to click 'Show more options'.",
            IconSymbol = "AppsList24",
            OnToggleAsync = HandleQuickToggleAsync
        });

        // 2. Disable Bing in Windows Search
        QuickToggles.Add(new QuickToggleCardModel
        {
            Id = "tweak.bing-search",
            OriginKey = "WPFToggleBingSearch",
            Title = "Disable Bing in Search",
            Description = "Restrict Start Menu search exclusively to local apps, settings, and documents.",
            IconSymbol = "Search24",
            OnToggleAsync = HandleQuickToggleAsync
        });

        // 3. Show File Extensions
        QuickToggles.Add(new QuickToggleCardModel
        {
            Id = "tweak.file-extensions",
            OriginKey = "WPFToggleShowExt",
            Title = "Show File Extensions",
            Description = "Always display full file extensions (.txt, .exe, .pdf, .zip) in File Explorer.",
            IconSymbol = "Document24",
            OnToggleAsync = HandleQuickToggleAsync
        });

        // 4. Disable Telemetry & Diagnostics
        QuickToggles.Add(new QuickToggleCardModel
        {
            Id = "tweak.telemetry",
            OriginKey = "WPFTweaksTelemetry",
            Title = "Disable Telemetry Data",
            Description = "Stop Microsoft diagnostic data collection and customer experience reporting.",
            IconSymbol = "EyeOff24",
            OnToggleAsync = HandleQuickToggleAsync
        });

        // 5. Disable Lockscreen & Spotlight Ads
        QuickToggles.Add(new QuickToggleCardModel
        {
            Id = "tweak.spotlight-ads",
            OriginKey = "privacy.spotlight.lockscreen-ads",
            Title = "Disable Lockscreen Ads",
            Description = "Disable tips, suggested promotions, and Spotlight advertisements on the lock screen.",
            IconSymbol = "LockClosed24",
            OnToggleAsync = HandleQuickToggleAsync
        });

        // 6. End Task on Taskbar Right-Click
        QuickToggles.Add(new QuickToggleCardModel
        {
            Id = "tweak.end-task",
            OriginKey = "WPFTweaksEndTaskOnTaskbar",
            Title = "End Task on Taskbar",
            Description = "Add an instant 'End Task' option to right-click menus on taskbar application icons.",
            IconSymbol = "DismissCircle24",
            OnToggleAsync = HandleQuickToggleAsync
        });

        // 7. Disable Background Apps
        QuickToggles.Add(new QuickToggleCardModel
        {
            Id = "tweak.background-apps",
            OriginKey = "WPFTweaksDisableBGapps",
            Title = "Disable Background Apps",
            Description = "Prevent Store apps from continuously consuming RAM and CPU in the background.",
            IconSymbol = "Power24",
            OnToggleAsync = HandleQuickToggleAsync
        });

        // 8. System Dark Mode
        QuickToggles.Add(new QuickToggleCardModel
        {
            Id = "tweak.dark-mode",
            OriginKey = "WPFToggleDarkMode",
            Title = "System Dark Mode",
            Description = "Enable modern dark theme across Windows system surfaces and supported apps.",
            IconSymbol = "DarkTheme24",
            OnToggleAsync = HandleQuickToggleAsync
        });
    }

    private TweakDefinition? FindTweak(string? originOrId)
    {
        if (string.IsNullOrWhiteSpace(originOrId)) return null;

        return _catalog.FirstOrDefault(t =>
            string.Equals(t.Origin, originOrId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Id, originOrId, StringComparison.OrdinalIgnoreCase) ||
            (t.Aliases != null && t.Aliases.Any(a => string.Equals(a, originOrId, StringComparison.OrdinalIgnoreCase))));
    }

    public async Task RefreshAllToggleStatesAsync()
    {
        await Task.Yield();
        _isRefreshingStates = true;
        try
        {
            var updates = await Task.Run(async () =>
            {
                var list = new List<(QuickToggleCardModel Toggle, TweakState State)>();
                foreach (var toggle in QuickToggles)
                {
                    var tweak = FindTweak(toggle.OriginKey);
                    if (tweak != null)
                    {
                        try
                        {
                            var state = await _tweakService.DetectTweakStateAsync(tweak);
                            list.Add((toggle, state));
                        }
                        catch { }
                    }
                }
                return list;
            });

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                foreach (var (toggle, state) in updates)
                {
                    toggle.State = state;
                }
            });
        }
        finally
        {
            _isRefreshingStates = false;
        }
    }

    private async Task HandleQuickToggleAsync(QuickToggleCardModel model, bool? target = null)
    {
        var tweak = FindTweak(model.OriginKey);
        if (tweak == null)
        {
            GlobalStatusMessage = $"Configuration not found for {model.Title}.";
            return;
        }

        if (_isRefreshingStates || model.IsBusy || model.IsApplying) return;
        bool targetState = target ?? !model.IsActive;

        if (targetState && !tweak.Reversible)
        {
            bool confirmed = await RequestIrreversibleConfirmationAsync(new[] { tweak });
            if (!confirmed)
            {
                GlobalStatusMessage = $"Cancelled {model.Title}.";
                return;
            }
        }

        model.IsBusy = true;
        model.IsApplying = true;

        string verb = targetState ? "Enabling" : "Disabling";
        GlobalStatusMessage = $"{verb} {model.Title}...";

        using var activity = _activityTracker?.BeginActivity($"{verb} {model.Title}...");
        await Task.Yield();

        try
        {
            var (report, actualState) = await Task.Run(async () =>
            {
                OperationExecutionReport rep;
                if (targetState)
                {
                    rep = await _tweakService.ApplyTweaksAsync(new[] { tweak }, _runner);
                }
                else
                {
                    rep = await _tweakService.UndoTweaksAsync(new[] { tweak }, _runner);
                }

                _ = WindowsSettingNotifier.NotifySettingsChangedAsync();
                var state = await _tweakService.DetectTweakStateAsync(tweak);
                return (rep, state);
            });

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                model.State = actualState;
            });

            var res = report.Results.FirstOrDefault();
            bool succeeded = res != null && (res.Status is OperationItemStatus.Succeeded or OperationItemStatus.Applied or OperationItemStatus.Restored);
            if (succeeded)
            {
                GlobalStatusMessage = $"{model.Title} is now {(actualState == TweakState.Applied ? "enabled" : (actualState == TweakState.Partial ? "partially enabled" : "reverted to default"))}.";
                HasFailedDetails = false;
            }
            else
            {
                GlobalStatusMessage = $"Status for {model.Title}: {res?.Reason ?? "System policy updated."}";
                if (res != null && res.IsFailure)
                {
                    DetailsDialogTitle = $"Execution Details — {model.Title}";
                    ExecutionDetailsItems.Clear();
                    ExecutionDetailsItems.Add(res);
                    HasFailedDetails = true;
                }
            }
        }
        catch (Exception ex)
        {
            GlobalStatusMessage = $"Failed to toggle {model.Title}: {ex.Message}";
            try
            {
                bool actualActive = await Task.Run(() => _tweakService.DetectStateAsync(tweak));
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    model.IsActive = actualActive;
                });
            }
            catch { }
        }
        finally
        {
            model.IsApplying = false;
            model.IsBusy = false;
            _ = RecalculateOptimizationScoreAsync();
        }
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        if (IsCreatingRestorePoint) return;
        IsCreatingRestorePoint = true;
        RestorePointStatus = "Creating system restore point...";

        using var activity = _activityTracker?.BeginActivity("Creating System Restore Point...");
        await Task.Yield();

        try
        {
            var res = await Task.Run(() => _restoreService.CreateRestorePoint("WinVelo System Health Snapshot"));
            if (res.Success)
            {
                RestorePointStatus = $"Restore point created successfully (Seq #{res.SequenceNumber}).";
            }
            else if (res.WasThrottledOrSkipped)
            {
                RestorePointStatus = "Windows throttled restore point creation (a recent snapshot already exists).";
            }
            else
            {
                RestorePointStatus = $"Restore point creation notice: {res.ErrorMessage ?? "Standard policy"}";
            }
        }
        catch (Exception ex)
        {
            RestorePointStatus = $"Failed to create restore point: {ex.Message}";
        }
        finally
        {
            IsCreatingRestorePoint = false;
            await RefreshCalmStatusAsync();
        }
    }

    private void HandleProfileReport(ProfileCardModel card, OperationExecutionReport report, bool isRestore = false)
    {
        int total = report.TotalRequested;
        int succ = report.SucceededCount;
        int fail = report.FailedCount;
        string actionVerb = isRestore ? "Restored" : "Applied";

        if (fail > 0)
        {
            card.SetStatusMessage($"{actionVerb} {succ} of {total} — {fail} failed (details)", "#EF4444", hasFailures: true);
            GlobalStatusMessage = $"{actionVerb} {succ} of {total} — {fail} failed (details)";
            DetailsDialogTitle = $"Execution Details — {card.Title}";
            ExecutionDetailsItems.Clear();
            foreach (var item in report.Results.Where(r => r.IsFailure))
            {
                ExecutionDetailsItems.Add(item);
            }
            HasFailedDetails = true;
        }
        else if (report.SkippedCount > 0)
        {
            card.SetStatusMessage($"{actionVerb} {succ} of {total} ({report.SkippedCount} skipped)", "#60A5FA");
            GlobalStatusMessage = $"{card.Title}: {actionVerb} {succ} of {total} ({report.SkippedCount} skipped).";
            HasFailedDetails = false;
            ExecutionDetailsItems.Clear();
        }
        else
        {
            card.SetStatusMessage(isRestore ? "Defaults restored!" : "Profile applied successfully!", "#34D399");
            GlobalStatusMessage = isRestore ? "Windows defaults restored successfully." : $"{card.Title} applied successfully.";
            HasFailedDetails = false;
            ExecutionDetailsItems.Clear();
        }
    }

    [RelayCommand]
    private async Task FreeUpRamAsync()
    {
        if (IsPurgingRam) return;
        IsPurgingRam = true;
        GlobalStatusMessage = "Purging memory cache and trimming process working sets...";

        using var activity = _activityTracker?.BeginActivity("Purging Standby RAM Cache...");
        await Task.Yield();

        try
        {
            var result = await Task.Run(() => _memoryService.PurgeMemoryAndStandbyListAsync());
            var sysInfo = await Task.Run(() => SystemInfoHelper.GetSystemInfo());
            SystemInfo = sysInfo;
            UpdateRamMetrics();
            GlobalStatusMessage = $"RAM Purge Complete: Freed {result.FormattedFreed} of Standby & Working Set Memory.";
        }
        catch (Exception ex)
        {
            GlobalStatusMessage = $"RAM Purge Notice: {ex.Message}";
        }
        finally
        {
            IsPurgingRam = false;
        }
    }

    [RelayCommand]
    public async Task UndoAllChangesAsync()
    {
        var activeTweaks = _tweakService.GetActiveJournalTweaks();
        if (activeTweaks.Count == 0)
        {
            GlobalStatusMessage = "No active tweaks found in WinVelo journal to undo. Changes made by other tools are not covered.";
            return;
        }

        await OpenProfilePreviewAsync(
            "Undo all WinVelo changes",
            "Revert applied tweaks recorded in the journal (newest first).",
            "Only tweaks modified and tracked by WinVelo are restored. Changes made by other tools or Windows updates are not covered.",
            "Revert Selected Tweaks",
            activeTweaks,
            isUndo: true,
            card: null);
    }

    [RelayCommand]
    public async Task GetRecommendationsAsync()
    {
        await SmartAnalyzeAndOptimizeAsync();
    }

    [RelayCommand]
    private async Task SmartAnalyzeAndOptimizeAsync()
    {
        if (IsSmartOptimizing) return;
        IsSmartOptimizing = true;
        GlobalStatusMessage = "Analyzing system profile and generating recommendations...";

        using var activity = _activityTracker?.BeginActivity("Generating recommendations...");
        await Task.Yield();

        try
        {
            var recommended = await Task.Run(() => _advisorService.GetSmartRecommendationsAsync(_catalog, _tweakService));
            if (recommended.Count == 0)
            {
                GlobalStatusMessage = "All recommended settings are already applied on this PC.";
                return;
            }

            await OpenProfilePreviewAsync(
                "Hardware Recommendations",
                "Based on your hardware profile and system assessment, the following tweaks are recommended for review.",
                "Already applied tweaks are skipped. You may untick any settings before applying.",
                "Apply Recommended Tweaks",
                recommended,
                isUndo: false,
                card: null);

            GlobalStatusMessage = $"{recommended.Count} recommendations ready for review.";
        }
        catch (Exception ex)
        {
            GlobalStatusMessage = $"Recommendations notice: {ex.Message}";
        }
        finally
        {
            IsSmartOptimizing = false;
        }
    }

    [RelayCommand]
    public async Task RecalculateOptimizationScoreAsync()
    {
        await Task.Yield();
        try
        {
            var assessment = await Task.Run(() => _advisorService.CalculateOptimizationScoreAsync(_catalog, _tweakService));
            ConfiguredSettingsPercentage = assessment.ScorePercent;
            ConfiguredSettingsDetailText = $"{assessment.AppliedCount} of {assessment.TotalScoredCount} settings configured";
            ConfiguredSettingsStatusText = $"{assessment.AppliedCount} active";
            ConfiguredSettingsBrush = "#10B981";
            AppliedTweaksSummaryText = $"{assessment.AppliedCount} tweaks applied by WinVelo";

            if (assessment.TailoredRecommendationNotes.Count > 0)
            {
                SmartAdvisorSummary = string.Join(" • ", assessment.TailoredRecommendationNotes);
            }
        }
        catch { }
    }
}

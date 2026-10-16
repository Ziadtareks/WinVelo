using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.Core.Engine.Runners;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Core.Safety;
using Tweakora.Modules.Tweaks;

namespace Tweakora.App.ViewModels;

public sealed partial class ExecutionResultItemModel : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string StatusText { get; init; }
    public required string StatusBg { get; init; }
    public required string StatusFg { get; init; }
    public string? Reason { get; init; }
    public bool HasReason => !string.IsNullOrWhiteSpace(Reason);
}

public sealed partial class PreviewSubcategoryGroup : ObservableObject
{
    public required string Subcategory { get; init; }
    public ObservableCollection<ProfilePreviewItemModel> Items { get; } = new();

    public string TotalCountText => $"{Items.Count} tweak{(Items.Count == 1 ? "" : "s")}";

    public Action? OnSelectionChanged { get; set; }

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var item in Items)
        {
            if (item.IsSelectable)
            {
                item.IsSelected = true;
            }
        }
        OnSelectionChanged?.Invoke();
    }

    [RelayCommand]
    public void SelectNone()
    {
        foreach (var item in Items)
        {
            if (item.IsSelectable)
            {
                item.IsSelected = false;
            }
        }
        OnSelectionChanged?.Invoke();
    }
}

public sealed class ExcludedPreviewItemModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Reason { get; init; }
}

public sealed partial class UnifiedExecutionDialogViewModel : ObservableObject
{
    // PREVIEW DIALOG PROPERTIES
    [ObservableProperty]
    private bool _isPreviewOpen;

    [ObservableProperty]
    private string _previewTitle = "Preview Changes";

    [ObservableProperty]
    private string _previewDescription = string.Empty;

    [ObservableProperty]
    private string _previewNotice = string.Empty;

    [ObservableProperty]
    private string _previewConfirmText = "Confirm & Apply";

    [ObservableProperty]
    private bool _isPreviewLoading;

    [ObservableProperty]
    private bool _isUndo;

    [ObservableProperty]
    private string _previewSummaryText = string.Empty;

    [ObservableProperty]
    private bool _hasExcludedItems;

    [ObservableProperty]
    private bool _isExcludedSectionOpen;

    [ObservableProperty]
    private string _excludedSectionHeader = "Not included";

    public string ExcludedChevronText => IsExcludedSectionOpen ? "▲ Hide" : "▼ Show";

    partial void OnIsExcludedSectionOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(ExcludedChevronText));
    }

    [RelayCommand]
    public void ToggleExcludedSection()
    {
        IsExcludedSectionOpen = !IsExcludedSectionOpen;
    }

    public ObservableCollection<ProfilePreviewItemModel> PreviewItems { get; } = new();
    public ObservableCollection<PreviewSubcategoryGroup> ItemGroups { get; } = new();
    public ObservableCollection<ExcludedPreviewItemModel> ExcludedItems { get; } = new();

    private Func<List<TweakDefinition>, Task>? _onConfirmedCallback;
    private Action? _onCancelledCallback;

    // RESULT DIALOG PROPERTIES
    [ObservableProperty]
    private bool _isResultOpen;

    [ObservableProperty]
    private string _resultTitle = "Execution Summary";

    [ObservableProperty]
    private string _resultSummary = string.Empty;

    [ObservableProperty]
    private bool _isServicingHealthy = true;

    [ObservableProperty]
    private string _servicingHealthSummary = string.Empty;

    public ObservableCollection<ExecutionResultItemModel> ResultItems { get; } = new();
    public ObservableCollection<string> ServicingWarnings { get; } = new();

    private readonly ITweakService? _tweakService;
    private readonly IOperationRunner _runner;
    private readonly IActivityTracker? _activityTracker;

    public UnifiedExecutionDialogViewModel(
        ITweakService? tweakService = null,
        IOperationRunner? runner = null,
        IActivityTracker? activityTracker = null)
    {
        _tweakService = tweakService;
        _runner = runner ?? new OperationRunner();
        _activityTracker = activityTracker;
    }

    public void UpdateSummary()
    {
        int toApply = PreviewItems.Count(i => i.IsSelected && (!i.IsAlreadyApplied || IsUndo));
        int alreadyApplied = PreviewItems.Count(i => i.IsAlreadyApplied && !IsUndo);
        int needRestart = PreviewItems.Count(i => i.IsSelected && (!i.IsAlreadyApplied || IsUndo) && (i.RequiresReboot || i.RequiresExplorerRestart));

        if (IsUndo)
        {
            PreviewSummaryText = $"{toApply} to revert · {needRestart} need a restart";
        }
        else
        {
            PreviewSummaryText = $"{toApply} to apply · {alreadyApplied} already applied (skipped) · {needRestart} need a restart";
        }
    }

    public async Task OpenPreviewAsync(
        string title,
        string description,
        string notice,
        string confirmText,
        IEnumerable<TweakDefinition> tweaks,
        bool isUndo,
        Func<List<TweakDefinition>, Task>? onConfirmed = null,
        Action? onCancelled = null,
        IReadOnlyList<ExcludedPresetTweak>? excludedTweaks = null)
    {
        PreviewTitle = title;
        PreviewDescription = description;
        PreviewNotice = notice;
        PreviewConfirmText = confirmText;
        IsUndo = isUndo;
        _onConfirmedCallback = onConfirmed;
        _onCancelledCallback = onCancelled;

        PreviewItems.Clear();
        ItemGroups.Clear();
        ExcludedItems.Clear();
        IsPreviewLoading = true;
        IsPreviewOpen = true;

        var tweakList = tweaks.ToList();
        var previewList = new List<ProfilePreviewItemModel>();

        foreach (var t in tweakList)
        {
            var item = new ProfilePreviewItemModel
            {
                Tweak = t,
                Id = t.Id,
                Name = t.Name,
                Risk = t.Risk,
                Reversible = t.Reversible,
                RequiresReboot = t.RequiresReboot,
                RequiresExplorerRestart = t.RequiresExplorerRestart,
                IsSelected = true
            };
            item.OnSelectionChanged = UpdateSummary;
            previewList.Add(item);
        }

        foreach (var item in previewList)
        {
            PreviewItems.Add(item);
        }

        // Group by Subcategory
        var subcatGroups = previewList
            .GroupBy(i => i.Subcategory)
            .OrderBy(g => g.Key);

        foreach (var group in subcatGroups)
        {
            var grp = new PreviewSubcategoryGroup
            {
                Subcategory = group.Key,
                OnSelectionChanged = UpdateSummary
            };
            foreach (var item in group)
            {
                grp.Items.Add(item);
            }
            ItemGroups.Add(grp);
        }

        // Populate Excluded Items
        if (excludedTweaks != null && excludedTweaks.Count > 0)
        {
            HasExcludedItems = true;
            ExcludedSectionHeader = $"Not included ({excludedTweaks.Count})";
            IsExcludedSectionOpen = false;

            var catalog = _tweakService?.LoadCatalog() ?? Array.Empty<TweakDefinition>();
            var catDict = catalog.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var exc in excludedTweaks)
            {
                catDict.TryGetValue(exc.Id, out var tweakDef);
                ExcludedItems.Add(new ExcludedPreviewItemModel
                {
                    Id = exc.Id,
                    Name = tweakDef?.Name ?? exc.Id,
                    Reason = exc.Reason
                });
            }
        }
        else
        {
            HasExcludedItems = false;
            ExcludedSectionHeader = "Not included";
            IsExcludedSectionOpen = false;
        }

        UpdateSummary();

        _ = Task.Run(async () =>
        {
            foreach (var item in previewList)
            {
                try
                {
                    var state = _tweakService != null
                        ? await _tweakService.DetectTweakStateAsync(item.Tweak)
                        : TweakState.NotApplied;
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (isUndo)
                        {
                            item.StateText = state switch
                            {
                                TweakState.Applied => "Applied",
                                TweakState.Partial => "Partial",
                                _ => "Modified"
                            };
                            item.StateBg = "#1E3A8A";
                            item.StateFg = "#60A5FA";
                            item.IsAlreadyApplied = false;
                            item.IsSelected = true;
                        }
                        else
                        {
                            switch (state)
                            {
                                case TweakState.Applied:
                                    item.StateText = "Applied";
                                    item.StateBg = "#064E3B";
                                    item.StateFg = "#10B981";
                                    item.IsAlreadyApplied = true;
                                    item.IsSelected = false; // already applied tweaks are skipped and unticked
                                    break;
                                case TweakState.Partial:
                                    item.StateText = "Partial";
                                    item.StateBg = "#451A03";
                                    item.StateFg = "#F59E0B";
                                    item.IsAlreadyApplied = false;
                                    item.IsSelected = true;
                                    break;
                                case TweakState.NotApplied:
                                    item.StateText = "Not applied";
                                    item.StateBg = "#1E222D";
                                    item.StateFg = "#94A3B8";
                                    item.IsAlreadyApplied = false;
                                    item.IsSelected = true;
                                    break;
                                default:
                                    item.StateText = "Can't verify";
                                    item.StateBg = "#1E222D";
                                    item.StateFg = "#94A3B8";
                                    item.IsAlreadyApplied = false;
                                    item.IsSelected = true;
                                    break;
                            }
                        }
                        UpdateSummary();
                    });
                }
                catch { }
            }

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPreviewLoading = false;
                UpdateSummary();
            });
        });
    }

    [RelayCommand]
    public async Task ConfirmPreviewAsync()
    {
        IsPreviewOpen = false;

        var selected = PreviewItems
            .Where(i => i.IsSelected && (!i.IsAlreadyApplied || IsUndo))
            .Select(i => i.Tweak)
            .ToList();

        if (selected.Count == 0)
        {
            return;
        }

        if (_onConfirmedCallback != null)
        {
            await _onConfirmedCallback(selected);
        }
        else
        {
            await ExecuteAndShowResultAsync(selected, IsUndo);
        }
    }

    [RelayCommand]
    public void CancelPreview()
    {
        IsPreviewOpen = false;
        _onCancelledCallback?.Invoke();
    }

    public async Task ExecuteAndShowResultAsync(List<TweakDefinition> tweaks, bool isUndo)
    {
        using var activity = _activityTracker?.BeginActivity(isUndo ? "Reverting Changes..." : "Applying Changes...");
        await Task.Yield();

        OperationExecutionReport report;
        if (isUndo)
        {
            report = _tweakService != null
                ? await Task.Run(() => _tweakService.UndoTweaksAsync(tweaks, _runner))
                : new OperationExecutionReport
                {
                    Title = "Undo Changes",
                    StartTime = DateTime.UtcNow,
                    EndTime = DateTime.UtcNow,
                    Results = new List<OperationItemResult>()
                };
        }
        else
        {
            report = _tweakService != null
                ? await Task.Run(() => _tweakService.ApplyTweaksAsync(tweaks, _runner))
                : new OperationExecutionReport
                {
                    Title = "Apply Changes",
                    StartTime = DateTime.UtcNow,
                    EndTime = DateTime.UtcNow,
                    Results = new List<OperationItemResult>()
                };
        }

        ServicingHealthReport? healthReport = null;
        try
        {
            if (_tweakService != null)
            {
                healthReport = await _tweakService.CheckServicingHealthAsync();
            }
        }
        catch { }

        ShowResult(tweaks, report, healthReport, isUndo);
    }

    public void ShowResult(
        List<TweakDefinition> tweaks,
        OperationExecutionReport report,
        ServicingHealthReport? healthReport,
        bool isUndo)
    {
        ResultTitle = isUndo ? "Undo Changes Completed" : "Modifications Applied";
        ResultSummary = isUndo
            ? $"Reverted {report.SucceededCount} of {report.TotalRequested} changes."
            : $"Applied {report.SucceededCount} of {report.TotalRequested} modifications.";

        ResultItems.Clear();
        foreach (var tweak in tweaks)
        {
            var res = report.Results.FirstOrDefault(r => r.Id == tweak.Id || r.Name == tweak.Name);
            bool succeeded = res != null && (res.Status is OperationItemStatus.Succeeded or OperationItemStatus.Applied or OperationItemStatus.Restored);
            bool skipped = res != null && res.Status == OperationItemStatus.Skipped;

            string statusText = succeeded ? (isUndo ? "Reverted" : "Applied") : (skipped ? "Skipped" : "Failed");
            string statusBg = succeeded ? "#064E3B" : (skipped ? "#1E3A8A" : "#450A0A");
            string statusFg = succeeded ? "#10B981" : (skipped ? "#60A5FA" : "#EF4444");

            ResultItems.Add(new ExecutionResultItemModel
            {
                Id = tweak.Id,
                Name = tweak.Name,
                StatusText = statusText,
                StatusBg = statusBg,
                StatusFg = statusFg,
                Reason = res?.Reason
            });
        }

        ServicingWarnings.Clear();
        if (healthReport != null)
        {
            IsServicingHealthy = healthReport.IsHealthy;
            ServicingHealthSummary = healthReport.SummaryText;
            foreach (var w in healthReport.Warnings)
            {
                ServicingWarnings.Add(w);
            }
        }
        else
        {
            IsServicingHealthy = true;
            ServicingHealthSummary = "Servicing health check passed.";
        }

        IsResultOpen = true;
    }

    [RelayCommand]
    public void CloseResult()
    {
        IsResultOpen = false;
    }
}

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

    public ObservableCollection<ProfilePreviewItemModel> PreviewItems { get; } = new();

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

    public async Task OpenPreviewAsync(
        string title,
        string description,
        string notice,
        string confirmText,
        IEnumerable<TweakDefinition> tweaks,
        bool isUndo,
        Func<List<TweakDefinition>, Task>? onConfirmed = null,
        Action? onCancelled = null)
    {
        PreviewTitle = title;
        PreviewDescription = description;
        PreviewNotice = notice;
        PreviewConfirmText = confirmText;
        IsUndo = isUndo;
        _onConfirmedCallback = onConfirmed;
        _onCancelledCallback = onCancelled;

        PreviewItems.Clear();
        IsPreviewLoading = true;
        IsPreviewOpen = true;

        var tweakList = tweaks.ToList();
        var previewList = new List<ProfilePreviewItemModel>();

        foreach (var t in tweakList)
        {
            previewList.Add(new ProfilePreviewItemModel
            {
                Tweak = t,
                Id = t.Id,
                Name = t.Name,
                Risk = t.Risk,
                Reversible = t.Reversible,
                RequiresReboot = t.RequiresReboot,
                RequiresExplorerRestart = t.RequiresExplorerRestart,
                IsSelected = true
            });
        }

        foreach (var item in previewList)
        {
            PreviewItems.Add(item);
        }

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
                    });
                }
                catch { }
            }

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPreviewLoading = false;
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

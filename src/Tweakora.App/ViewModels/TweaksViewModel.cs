using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.App.Localization;
using Tweakora.Core.Engine;
using Tweakora.Core.Engine.Handlers;
using Tweakora.Core.Fakes;
using Tweakora.Core.Models;

namespace Tweakora.App.ViewModels;

public sealed partial class TweakCardModel : ObservableObject
{
    public TweakDefinition Tweak { get; }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isStaged;

    [ObservableProperty]
    private bool _stagedTargetState;

    [ObservableProperty]
    private bool _isBusy;

    public bool IsCheckedInUi
    {
        get => IsStaged ? StagedTargetState : IsActive;
        set
        {
            if (value == IsActive)
            {
                IsStaged = false;
            }
            else
            {
                IsStaged = true;
                StagedTargetState = value;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStaged));
        }
    }

    public void DiscardStaged()
    {
        IsStaged = false;
        OnPropertyChanged(nameof(IsCheckedInUi));
    }

    public void MarkApplied(bool active)
    {
        IsActive = active;
        IsStaged = false;
        OnPropertyChanged(nameof(IsCheckedInUi));
    }

    public string Id => Tweak.Id;
    public string Category => Tweak.Category;
    public string Risk => Tweak.Risk;
    public bool RequiresReboot => Tweak.RequiresReboot;
    public bool RequiresExplorerRestart => Tweak.RequiresExplorerRestart;
    public string SourceUrl => Tweak.Source.ValueKind == JsonValueKind.String ? Tweak.Source.GetString() ?? "" : "";

    public string LocalizedName => Tweak.Name;
    public string LocalizedDescription => Tweak.Description;
    public string? LocalizedEffectNote => Tweak.EffectNote;
    public string? LocalizedWarning => Tweak.Warnings;

    public bool HasEffectNote => !string.IsNullOrWhiteSpace(LocalizedEffectNote);
    public bool HasWarning => !string.IsNullOrWhiteSpace(LocalizedWarning);

    public string RiskBadgeBackground => Risk switch
    {
        "Safe" => "#E6F4EA",
        "Low" => "#E8F0FE",
        "Medium" => "#FEF7E0",
        "High" => "#FCE8E6",
        _ => "#F1F3F4"
    };

    public string RiskBadgeForeground => Risk switch
    {
        "Safe" => "#137333",
        "Low" => "#1A73E8",
        "Medium" => "#B06000",
        "High" => "#C5221F",
        _ => "#5F6368"
    };

    public TweakCardModel(TweakDefinition tweak)
    {
        Tweak = tweak;
        LocalizationManager.Instance.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(LocalizedName));
            OnPropertyChanged(nameof(LocalizedDescription));
            OnPropertyChanged(nameof(LocalizedEffectNote));
            OnPropertyChanged(nameof(LocalizedWarning));
            OnPropertyChanged(nameof(HasEffectNote));
            OnPropertyChanged(nameof(HasWarning));
        };
    }
}

public sealed partial class TweaksViewModel : ViewModelBase
{
    private readonly List<TweakCardModel> _allTweaks = new();
    private readonly StateDetector _detector;
    private readonly TweakRunner _runner;
    private readonly DryRunEngine _dryRun;

    public ObservableCollection<TweakCardModel> FilteredTweaks { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private string _selectedRisk = "All";

    [ObservableProperty]
    private TweakCardModel? _selectedTweakForDetails;

    [ObservableProperty]
    private bool _isDetailsPanelOpen;

    [ObservableProperty]
    private string _previewReportText = string.Empty;

    [ObservableProperty]
    private bool _isPreviewDialogOpen;

    [ObservableProperty]
    private int _stagedCount;

    [ObservableProperty]
    private bool _hasStagedChanges;

    [ObservableProperty]
    private bool _hasPageError;

    [ObservableProperty]
    private string _pageErrorMessage = string.Empty;

    public TweaksViewModel()
    {
        // Safe in-memory simulated platform environment for non-destructive local host execution
        FakeRegistryService fakeReg = new();
        FakeServicesService fakeSvc = new();
        FakeTaskSchedulerService fakeTask = new();
        FakeRestoreService fakeRestore = new();
        FakeShellService fakeShell = new();
        RollbackJournalManager journal = new(Path.Combine(Path.GetTempPath(), "Tweakora_App_Journal"));

        Core.Engine.ExecutionContext context = new()
        {
            Registry = fakeReg,
            Services = fakeSvc,
            Tasks = fakeTask,
            Shell = fakeShell,
            Journal = journal,
            IsDryRun = false
        };

        List<IActionHandler> handlers = new()
        {
            new RegistryActionHandler(),
            new ServiceActionHandler(),
            new TaskActionHandler(),
            new FileActionHandler()
        };

        _detector = new StateDetector(fakeReg, fakeSvc, fakeTask);
        _runner = new TweakRunner(handlers, context, fakeRestore, _detector);
        _dryRun = new DryRunEngine(handlers, context);

        LoadCatalogTweaks();
    }

    private void LoadCatalogTweaks()
    {
        try
        {
            string rootDir = AppDomain.CurrentDomain.BaseDirectory;
            string tweaksDir = Path.Combine(rootDir, "Catalog", "Tweaks");
            if (!Directory.Exists(tweaksDir))
            {
                string? parent = Directory.GetParent(rootDir)?.FullName;
                while (parent != null)
                {
                    string candidate = Path.Combine(parent, "Catalog", "Tweaks");
                    if (Directory.Exists(candidate))
                    {
                        tweaksDir = candidate;
                        break;
                    }
                    parent = Directory.GetParent(parent)?.FullName;
                }
            }

            if (Directory.Exists(tweaksDir))
            {
                foreach (string file in Directory.GetFiles(tweaksDir, "*.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        List<TweakDefinition>? list = JsonSerializer.Deserialize<List<TweakDefinition>>(json);
                        if (list != null)
                        {
                            foreach (TweakDefinition t in list)
                            {
                                var card = new TweakCardModel(t);
                                card.PropertyChanged += (s, e) =>
                                {
                                    if (e.PropertyName == nameof(TweakCardModel.IsStaged))
                                    {
                                        UpdateStagedState();
                                    }
                                };
                                _allTweaks.Add(card);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Failed to read tweak file: {File}", file);
                    }
                }
            }

            ApplyFilter();
        }
        catch (Exception ex)
        {
            HasPageError = true;
            PageErrorMessage = $"Error loading tweaks: {ex.Message}";
            Serilog.Log.Error(ex, "Failed to load catalog");
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnSelectedRiskChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void SelectCategory(string category)
    {
        SelectedCategory = category;
    }

    private void ApplyFilter()
    {
        FilteredTweaks.Clear();

        IEnumerable<TweakCardModel> query = _allTweaks;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            query = query.Where(t =>
                t.LocalizedName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                t.LocalizedDescription.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                t.Id.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != "All")
        {
            query = query.Where(t => t.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SelectedRisk) && SelectedRisk != "All")
        {
            query = query.Where(t => t.Risk.Equals(SelectedRisk, StringComparison.OrdinalIgnoreCase));
        }

        foreach (TweakCardModel item in query)
        {
            FilteredTweaks.Add(item);
        }
    }

    [RelayCommand]
    private void ToggleTweak(TweakCardModel card)
    {
        // Strictly staging in-memory only. NEVER touches real PC or calls runner apply.
        if (card.IsStaged)
        {
            card.IsStaged = false;
            card.IsCheckedInUi = card.IsActive;
        }
        else
        {
            card.IsStaged = true;
            card.StagedTargetState = !card.IsActive;
            card.IsCheckedInUi = card.StagedTargetState;
        }
        UpdateStagedState();
    }

    private void UpdateStagedState()
    {
        StagedCount = _allTweaks.Count(t => t.IsStaged);
        HasStagedChanges = StagedCount > 0;
    }

    [RelayCommand]
    private void DiscardStagedChanges()
    {
        foreach (var card in _allTweaks.Where(t => t.IsStaged))
        {
            card.DiscardStaged();
        }
        UpdateStagedState();
        Serilog.Log.Information("Discarded all staged tweaks.");
    }

    [RelayCommand]
    private void ApplyStagedChanges()
    {
        var staged = _allTweaks.Where(t => t.IsStaged).ToList();
        if (staged.Count == 0) return;

        List<TweakDefinition> tweaksToPreview = staged.Select(c => c.Tweak).ToList();
        List<TweakPreview> previews = _dryRun.GeneratePreview(tweaksToPreview);

        StringWriter sw = new();
        sw.WriteLine($"Diff Preview for {staged.Count} Staged Tweak(s):\n");

        foreach (var card in staged)
        {
            string actionVerb = card.StagedTargetState ? "ENABLE / APPLY" : "REVERT / DISABLE";
            sw.WriteLine($"• {card.LocalizedName} [{card.Risk}] ({card.Id}) -> Target: {actionVerb}");

            var p = previews.FirstOrDefault(pr => pr.TweakId == card.Id);
            if (p != null)
            {
                foreach (var a in p.Actions)
                {
                    sw.WriteLine($"    - {a.ActionType} {a.Target}");
                    sw.WriteLine($"      Operation: {a.Operation} | Current: {a.CurrentState} -> Proposed: {a.ProposedState}");
                }
            }
            sw.WriteLine();
        }

        PreviewReportText = sw.ToString();
        IsPreviewDialogOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmApplyStagedAsync()
    {
        var staged = _allTweaks.Where(t => t.IsStaged).ToList();
        if (staged.Count == 0)
        {
            IsPreviewDialogOpen = false;
            return;
        }

        try
        {
            foreach (var card in staged)
            {
                card.IsBusy = true;
                try
                {
                    if (card.StagedTargetState)
                    {
                        var report = await _runner.ApplyBatchAsync(new[] { card.Tweak });
                        if (report.AppliedCount > 0)
                        {
                            card.MarkApplied(true);
                        }
                    }
                    else
                    {
                        await _runner.UndoTweakAsync(card.Tweak);
                        card.MarkApplied(false);
                    }
                }
                finally
                {
                    card.IsBusy = false;
                }
            }

            UpdateStagedState();
            IsPreviewDialogOpen = false;
            Serilog.Log.Information("Applied {Count} staged tweaks successfully in test environment.", staged.Count);
        }
        catch (Exception ex)
        {
            HasPageError = true;
            PageErrorMessage = $"Failed to apply staged tweaks: {ex.Message}";
            Serilog.Log.Error(ex, "Failed to apply staged tweaks");
        }
    }

    [RelayCommand]
    private void ShowDetails(TweakCardModel card)
    {
        SelectedTweakForDetails = card;
        IsDetailsPanelOpen = true;
    }

    [RelayCommand]
    private void CloseDetails()
    {
        IsDetailsPanelOpen = false;
    }

    [RelayCommand]
    private void GenerateDryRunPreview()
    {
        List<TweakDefinition> tweaksToPreview = FilteredTweaks.Select(c => c.Tweak).ToList();
        List<TweakPreview> previews = _dryRun.GeneratePreview(tweaksToPreview);

        StringWriter sw = new();
        sw.WriteLine($"Dry-Run Preview for {previews.Count} Tweak(s):\n");

        foreach (var p in previews)
        {
            sw.WriteLine($"• {p.Name} [{p.Risk}] ({p.TweakId}):");
            foreach (var a in p.Actions)
            {
                sw.WriteLine($"    - {a.ActionType} {a.Target}");
                sw.WriteLine($"      Operation: {a.Operation} | Current: {a.CurrentState} -> Proposed: {a.ProposedState}");
            }
            sw.WriteLine();
        }

        PreviewReportText = sw.ToString();
        IsPreviewDialogOpen = true;
    }

    [RelayCommand]
    private void ClosePreviewDialog()
    {
        IsPreviewDialogOpen = false;
    }
}

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
    private bool _isBusy;

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
                            _allTweaks.Add(new TweakCardModel(t));
                        }
                    }
                }
                catch { }
            }
        }

        ApplyFilter();
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
    private async Task ToggleTweakAsync(TweakCardModel card)
    {
        if (card.IsBusy) return;
        card.IsBusy = true;

        try
        {
            if (card.IsActive)
            {
                // Revert
                await _runner.UndoTweakAsync(card.Tweak);
                card.IsActive = false;
                Serilog.Log.Information("Undone tweak: {TweakId}", card.Id);
            }
            else
            {
                // Apply
                BatchExecutionReport report = await _runner.ApplyBatchAsync(new[] { card.Tweak });
                if (report.AppliedCount > 0)
                {
                    card.IsActive = true;
                    Serilog.Log.Information("Applied tweak: {TweakId}", card.Id);
                }
            }
        }
        finally
        {
            card.IsBusy = false;
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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.Core.Models;
using Tweakora.Core.Operations;
using Tweakora.Modules.Tweaks;

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
    public string? Origin => Tweak.Origin;
    public string Category => Tweak.Category;
    public string? Subcategory => Tweak.Subcategory;
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
    public bool IsHighRisk => string.Equals(Risk, "High", StringComparison.OrdinalIgnoreCase);
    public bool IsBitLocker => Id.Contains("bitlocker", StringComparison.OrdinalIgnoreCase) ||
                               (Origin?.Contains("bitlocker", StringComparison.OrdinalIgnoreCase) ?? false);

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
    }
}

public sealed class DnsOptionItem
{
    public required string Name { get; init; }
    public required string PrimaryDns { get; init; }
    public required string SecondaryDns { get; init; }
    public string DisplayText => $"{Name} ({PrimaryDns})";
}

public sealed partial class TweaksViewModel : ObservableObject
{
    private readonly ITweakService _tweakService;
    private readonly IOperationRunner _runner;
    private readonly List<TweakCardModel> _allTweaks = new();
    private readonly Dictionary<string, List<string>> _presets = new(StringComparer.OrdinalIgnoreCase);

    // 4 Display Collections
    public ObservableCollection<TweakCardModel> EssentialTweaks { get; } = new();
    public ObservableCollection<TweakCardModel> AdvancedTweaks { get; } = new();
    public ObservableCollection<TweakCardModel> PreferenceToggles { get; } = new();
    public ObservableCollection<TweakCardModel> ExtraTweaks { get; } = new();

    public ObservableCollection<DnsOptionItem> DnsOptions { get; } = new();
    public ObservableCollection<string> PerformancePlans { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private DnsOptionItem? _selectedDns;

    [ObservableProperty]
    private string _selectedPerformancePlan = "Balanced (Recommended)";

    [ObservableProperty]
    private bool _isPreviewDialogOpen;

    [ObservableProperty]
    private string _previewReportText = string.Empty;

    [ObservableProperty]
    private int _stagedCount;

    [ObservableProperty]
    private bool _hasStagedChanges;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunTweaksCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoSelectedTweaksCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Select tweaks or choose a preset to stage changes.";

    public TweaksViewModel(ITweakService tweakService, IOperationRunner runner)
    {
        _tweakService = tweakService;
        _runner = runner;

        InitializeDnsAndPlans();
        LoadPresets();
        LoadCatalogTweaks();
    }

    private void InitializeDnsAndPlans()
    {
        PerformancePlans.Add("Balanced (Recommended)");
        PerformancePlans.Add("High Performance");
        PerformancePlans.Add("Ultimate Performance");

        DnsOptions.Add(new DnsOptionItem { Name = "Default (DHCP)", PrimaryDns = "Automatic", SecondaryDns = "" });
        DnsOptions.Add(new DnsOptionItem { Name = "Cloudflare", PrimaryDns = "1.1.1.1", SecondaryDns = "1.0.0.1" });
        DnsOptions.Add(new DnsOptionItem { Name = "Quad9", PrimaryDns = "9.9.9.9", SecondaryDns = "149.112.112.112" });
        DnsOptions.Add(new DnsOptionItem { Name = "Google", PrimaryDns = "8.8.8.8", SecondaryDns = "8.8.4.4" });
        DnsOptions.Add(new DnsOptionItem { Name = "AdGuard", PrimaryDns = "94.140.14.14", SecondaryDns = "94.140.15.15" });
        DnsOptions.Add(new DnsOptionItem { Name = "OpenDNS", PrimaryDns = "208.67.222.222", SecondaryDns = "208.67.220.220" });

        SelectedDns = DnsOptions[0];
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
                Serilog.Log.Warning(ex, "Failed to load presets from preset.json");
            }
        }
    }

    private void LoadCatalogTweaks()
    {
        _allTweaks.Clear();
        var catalog = _tweakService.LoadCatalog();

        foreach (var tweak in catalog)
        {
            var card = new TweakCardModel(tweak);
            card.IsActive = _tweakService.DetectState(tweak);
            card.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TweakCardModel.IsStaged))
                {
                    UpdateStagedState();
                }
            };
            _allTweaks.Add(card);
        }

        ApplyFilter();
    }

    public void SetSearchQuery(string query)
    {
        SearchQuery = query;
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    public void ApplyFilter()
    {
        EssentialTweaks.Clear();
        AdvancedTweaks.Clear();
        PreferenceToggles.Clear();
        ExtraTweaks.Clear();

        string q = SearchQuery?.Trim() ?? string.Empty;

        foreach (var card in _allTweaks)
        {
            if (!string.IsNullOrWhiteSpace(q))
            {
                bool matches = card.LocalizedName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               card.LocalizedDescription.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               card.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               (card.Origin?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
                if (!matches) continue;
            }

            string sub = card.Subcategory ?? "";
            string id = card.Id.ToLowerInvariant();

            if (sub.Contains("Essential", StringComparison.OrdinalIgnoreCase) || id.StartsWith("essential."))
            {
                EssentialTweaks.Add(card);
            }
            else if (sub.Contains("Advanced", StringComparison.OrdinalIgnoreCase) || id.StartsWith("advanced."))
            {
                AdvancedTweaks.Add(card);
            }
            else if (sub.Contains("Customize", StringComparison.OrdinalIgnoreCase) || id.StartsWith("pref."))
            {
                PreferenceToggles.Add(card);
            }
            else
            {
                ExtraTweaks.Add(card);
            }
        }
    }

    private void UpdateStagedState()
    {
        StagedCount = _allTweaks.Count(t => t.IsStaged);
        HasStagedChanges = StagedCount > 0;
    }

    [RelayCommand]
    public void ApplyPreset(string presetName)
    {
        ClearSelection();

        if (string.Equals(presetName, "Clear", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Cleared all selections.";
            return;
        }

        if (!_presets.TryGetValue(presetName, out var presetKeys))
        {
            StatusMessage = $"Preset {presetName} not found in presets config.";
            return;
        }

        var keySet = new HashSet<string>(presetKeys, StringComparer.OrdinalIgnoreCase);
        int matched = 0;

        foreach (var card in _allTweaks)
        {
            if (card.IsBitLocker) continue;

            bool matches = (card.Origin != null && keySet.Contains(card.Origin)) ||
                           keySet.Contains(card.Id);

            if (matches)
            {
                card.IsStaged = true;
                card.StagedTargetState = true;
                card.IsCheckedInUi = true;
                matched++;
            }
        }

        UpdateStagedState();
        StatusMessage = $"Applied preset '{presetName}' ({matched} tweaks staged).";
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var card in _allTweaks)
        {
            card.DiscardStaged();
        }
        UpdateStagedState();
        StatusMessage = "All staged changes cleared.";
    }

    [RelayCommand]
    public async Task OpenDiffPreviewAsync()
    {
        var staged = _allTweaks.Where(t => t.IsStaged).ToList();
        if (staged.Count == 0)
        {
            StatusMessage = "No tweaks currently staged to preview.";
            return;
        }

        List<TweakDefinition> tweaksToPreview = staged.Select(c => c.Tweak).ToList();
        PreviewReportText = await _tweakService.GenerateDryRunDiffAsync(tweaksToPreview);
        IsPreviewDialogOpen = true;
    }

    [RelayCommand]
    public void CloseDiffPreview()
    {
        IsPreviewDialogOpen = false;
    }

    private bool CanRunTweaks() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRunTweaks))]
    public async Task RunTweaksAsync()
    {
        var staged = _allTweaks.Where(t => t.IsStaged).ToList();
        if (staged.Count == 0)
        {
            StatusMessage = "No tweaks staged to run.";
            return;
        }

        IsPreviewDialogOpen = false;
        IsBusy = true;
        try
        {
            StatusMessage = $"Applying {staged.Count} tweak(s)...";

            var report = await _tweakService.ApplyTweaksAsync(staged.Select(c => c.Tweak).ToList(), _runner);

            foreach (var card in staged)
            {
                card.MarkApplied(card.StagedTargetState);
            }

            // Apply selected performance plan if customized
            if (!string.IsNullOrWhiteSpace(SelectedPerformancePlan))
            {
                await _tweakService.SetPerformancePlanAsync(SelectedPerformancePlan);
            }

            // Apply selected DNS if not default
            if (SelectedDns != null && SelectedDns.PrimaryDns != "Automatic")
            {
                await _tweakService.SetDnsAsync(SelectedDns.PrimaryDns, SelectedDns.SecondaryDns);
            }

            UpdateStagedState();
            StatusMessage = $"Execution completed: {report.Summary}. All protected by journal.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Execution error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunTweaks))]
    public async Task UndoSelectedTweaksAsync()
    {
        var active = _allTweaks.Where(t => t.IsCheckedInUi).ToList();
        if (active.Count == 0)
        {
            StatusMessage = "No tweaks selected to undo.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"Reverting {active.Count} tweak(s)...";

            var report = await _tweakService.UndoTweaksAsync(active.Select(c => c.Tweak).ToList(), _runner);

            foreach (var card in active)
            {
                card.MarkApplied(false);
            }

            UpdateStagedState();
            StatusMessage = $"Revert completed: {report.Summary}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Revert error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public List<string> GetSelectedTweakIds() => _allTweaks.Where(t => t.IsCheckedInUi).Select(t => t.Id).ToList();

    public void SetSelectedTweakIds(IEnumerable<string> ids)
    {
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        foreach (var card in _allTweaks)
        {
            if (idSet.Contains(card.Id) || (card.Origin != null && idSet.Contains(card.Origin)))
            {
                card.IsStaged = true;
                card.StagedTargetState = true;
                card.IsCheckedInUi = true;
            }
            else
            {
                card.DiscardStaged();
            }
        }
        UpdateStagedState();
    }
}

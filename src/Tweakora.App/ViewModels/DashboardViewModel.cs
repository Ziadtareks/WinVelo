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

namespace Tweakora.App.ViewModels;

public sealed partial class QuickToggleCardModel : ObservableObject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string IconSymbol { get; init; }
    public string? OriginKey { get; init; }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isBusy;

    public Func<QuickToggleCardModel, bool?, Task>? OnToggleAsync { get; set; }

    [RelayCommand]
    private async Task ToggleAsync(object? parameter)
    {
        if (IsBusy || OnToggleAsync == null) return;
        bool? target = parameter is bool b ? b : null;
        await OnToggleAsync(this, target);
    }
}

public sealed partial class ProfileCardModel : ObservableObject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string BadgeText { get; init; }
    public required string BadgeBg { get; init; }
    public required string BadgeFg { get; init; }
    public required string TweakCountText { get; init; }
    public required string ImpactText { get; init; }
    public required string ButtonText { get; init; }
    public required string IconSymbol { get; init; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public Func<ProfileCardModel, Task>? OnApplyAsync { get; set; }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (IsBusy || OnApplyAsync == null) return;
        await OnApplyAsync(this);
    }
}

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly ITweakService _tweakService;
    private readonly IOperationRunner _runner;
    private readonly ISystemRestoreService _restoreService;
    private readonly IMemoryOptimizerService _memoryService;
    private readonly ISmartAdvisorService _advisorService;
    private readonly List<TweakDefinition> _catalog = new();

    [ObservableProperty]
    private SystemInfoSummary _systemInfo = SystemInfoHelper.GetSystemInfo();

    public bool IsAdmin { get; } = AdminPrivilegeHelper.IsRunningAsAdministrator();

    public string AdminBadgeBg => IsAdmin ? "#064E3B" : "#7F1D1D";
    public string AdminBadgeFg => IsAdmin ? "#10B981" : "#F87171";
    public string AdminStatusText => IsAdmin ? "Administrator Active" : "Standard User (Limited)";

    [ObservableProperty]
    private int _optimizationScore = 65;

    [ObservableProperty]
    private string _optimizationScoreGrade = "Good";

    [ObservableProperty]
    private string _optimizationScoreBrush = "#10B981";

    [ObservableProperty]
    private string _optimizationScoreDetailText = "Scanning hardware...";

    [ObservableProperty]
    private string _smartAdvisorSummary = "Ready for 1-Click Hardware Optimization.";

    [ObservableProperty]
    private bool _isSmartOptimizing;

    [ObservableProperty]
    private bool _isPurgingRam;

    [ObservableProperty]
    private string _restorePointStatus = "Restore point ready.";

    [ObservableProperty]
    private bool _isCreatingRestorePoint;

    [ObservableProperty]
    private string _globalStatusMessage = "Ready. Select a 1-Click Profile or toggle individual optimizations below.";

    public ObservableCollection<ProfileCardModel> Profiles { get; } = new();
    public ObservableCollection<QuickToggleCardModel> QuickToggles { get; } = new();

    public DashboardViewModel(
        ITweakService tweakService,
        IOperationRunner runner,
        ISystemRestoreService restoreService,
        IMemoryOptimizerService memoryService,
        ISmartAdvisorService advisorService)
    {
        _tweakService = tweakService;
        _runner = runner;
        _restoreService = restoreService;
        _memoryService = memoryService;
        _advisorService = advisorService;

        try
        {
            _catalog.AddRange(_tweakService.LoadCatalog());
        }
        catch { }

        InitializeProfiles();
        InitializeQuickToggles();

        _ = Task.Run(async () =>
        {
            await RefreshAllToggleStatesAsync();
            await RecalculateOptimizationScoreAsync();
        });
    }

    private void InitializeProfiles()
    {
        Profiles.Clear();

        // 1. Everyday Speed & Clean Setup
        Profiles.Add(new ProfileCardModel
        {
            Id = "everyday",
            Title = "Everyday Speed & Clean Setup",
            Description = "Safely disable telemetry, Bing web search in Start, disk clutter, and optimize Windows responsiveness for daily work and browsing.",
            BadgeText = "SAFE & RECOMMENDED",
            BadgeBg = "#064E3B",
            BadgeFg = "#34D399",
            TweakCountText = "18 Tweaks",
            ImpactText = "Immediate Responsiveness",
            ButtonText = "Apply Everyday Boost",
            IconSymbol = "Flash24",
            OnApplyAsync = ApplyProfileEverydayAsync
        });

        // 2. Gaming & Max FPS Boost
        Profiles.Add(new ProfileCardModel
        {
            Id = "gaming",
            Title = "Gaming & Max FPS Boost",
            Description = "Maximize GPU/CPU priority for games, enable Game Mode, disable GameDVR recording, optimize power plan, and stop background task lag.",
            BadgeText = "MAX PERFORMANCE",
            BadgeBg = "#78350F",
            BadgeFg = "#FBBF24",
            TweakCountText = "14 Tweaks",
            ImpactText = "Lower Input Lag & Stutter",
            ButtonText = "Apply Gaming Boost",
            IconSymbol = "Games24",
            OnApplyAsync = ApplyProfileGamingAsync
        });

        // 3. Privacy & Anti-Spyware Shield
        Profiles.Add(new ProfileCardModel
        {
            Id = "privacy",
            Title = "Privacy & Anti-Spyware Shield",
            Description = "Lock down Windows data collection: disable telemetry, advertising ID, diagnostics, activity tracking, Cortana, and lockscreen promo ads.",
            BadgeText = "MAX PRIVACY",
            BadgeBg = "#1E3A8A",
            BadgeFg = "#60A5FA",
            TweakCountText = "16 Tweaks",
            ImpactText = "Zero Background Telemetry",
            ButtonText = "Apply Privacy Shield",
            IconSymbol = "ShieldCheckmark24",
            OnApplyAsync = ApplyProfilePrivacyAsync
        });

        // 4. Restore Windows Defaults
        Profiles.Add(new ProfileCardModel
        {
            Id = "defaults",
            Title = "Restore Windows Defaults",
            Description = "Safely revert applied tweaks back to standard Windows defaults. Restores default telemetry, search, and original system behavior.",
            BadgeText = "SAFE ROLLBACK",
            BadgeBg = "#374151",
            BadgeFg = "#E5E7EB",
            TweakCountText = "Rollback",
            ImpactText = "Standard System Defaults",
            ButtonText = "Restore Defaults",
            IconSymbol = "ArrowCounterclockwise24",
            OnApplyAsync = ApplyProfileRestoreDefaultsAsync
        });
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
            string.Equals(t.Id, originOrId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task RefreshAllToggleStatesAsync()
    {
        foreach (var toggle in QuickToggles)
        {
            var tweak = FindTweak(toggle.OriginKey);
            if (tweak != null)
            {
                try
                {
                    bool active = await _tweakService.DetectStateAsync(tweak);
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        toggle.IsActive = active;
                    });
                }
                catch { }
            }
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

        if (model.IsBusy) return;
        model.IsBusy = true;
        bool targetState = target ?? !model.IsActive;

        try
        {
            GlobalStatusMessage = $"{(targetState ? "Enabling" : "Disabling")} {model.Title}...";

            OperationExecutionReport report;
            if (targetState)
            {
                report = await _tweakService.ApplyTweaksAsync(new[] { tweak }, _runner);
            }
            else
            {
                report = await _tweakService.UndoTweaksAsync(new[] { tweak }, _runner);
            }

            bool actualActive = await _tweakService.DetectStateAsync(tweak);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                model.IsActive = actualActive;
            });

            var res = report.Results.FirstOrDefault();
            if (res != null && (res.Status == OperationItemStatus.Succeeded || actualActive == targetState))
            {
                GlobalStatusMessage = $"{model.Title} is now {(actualActive ? "enabled" : "reverted to default")}.";
            }
            else
            {
                GlobalStatusMessage = $"Status for {model.Title}: {res?.Reason ?? "System policy updated."}";
            }
        }
        catch (Exception ex)
        {
            GlobalStatusMessage = $"Failed to toggle {model.Title}: {ex.Message}";
            try
            {
                bool actualActive = await _tweakService.DetectStateAsync(tweak);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    model.IsActive = actualActive;
                });
            }
            catch { }
        }
        finally
        {
            model.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        if (IsCreatingRestorePoint) return;
        IsCreatingRestorePoint = true;
        RestorePointStatus = "Creating system restore point...";

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
        }
    }

    private async Task ApplyProfileEverydayAsync(ProfileCardModel card)
    {
        card.IsBusy = true;
        card.StatusMessage = "Applying Everyday Speed Profile...";
        GlobalStatusMessage = "Applying Everyday Speed & Clean Setup...";

        try
        {
            var targets = _catalog.Where(t =>
                string.Equals(t.Category, "Essential", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, "WPFTweaksTelemetry", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, "WPFToggleBingSearch", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, "WPFTweaksDiskCleanup", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, "WPFTweaksDeleteTempFiles", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, "WPFTweaksEndTaskOnTaskbar", StringComparison.OrdinalIgnoreCase)
            ).Take(18).ToList();

            if (targets.Count > 0)
            {
                await _tweakService.ApplyTweaksAsync(targets, _runner);
            }

            card.StatusMessage = "Profile applied successfully!";
            GlobalStatusMessage = "Everyday Speed & Clean Setup applied successfully.";
            await RefreshAllToggleStatesAsync();
        }
        catch (Exception ex)
        {
            card.StatusMessage = $"Error: {ex.Message}";
            GlobalStatusMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    private async Task ApplyProfileGamingAsync(ProfileCardModel card)
    {
        card.IsBusy = true;
        card.StatusMessage = "Applying Gaming & FPS Profile...";
        GlobalStatusMessage = "Applying Gaming & Max FPS Boost...";

        try
        {
            var targets = _catalog.Where(t =>
                string.Equals(t.Origin, "WPFToggleGameMode", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, "WPFTweaksBackgroundApps", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, "WPFTweaksUltimatePerformance", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, "WPFTweaksDVR", StringComparison.OrdinalIgnoreCase) ||
                (t.Description.Contains("Game", StringComparison.OrdinalIgnoreCase) && !string.Equals(t.Risk, "High", StringComparison.OrdinalIgnoreCase))
            ).Take(14).ToList();

            if (targets.Count > 0)
            {
                await _tweakService.ApplyTweaksAsync(targets, _runner);
            }

            card.StatusMessage = "Gaming boost active!";
            GlobalStatusMessage = "Gaming & Max FPS Boost applied successfully.";
            await RefreshAllToggleStatesAsync();
        }
        catch (Exception ex)
        {
            card.StatusMessage = $"Error: {ex.Message}";
            GlobalStatusMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    private async Task ApplyProfilePrivacyAsync(ProfileCardModel card)
    {
        card.IsBusy = true;
        card.StatusMessage = "Applying Privacy Shield Profile...";
        GlobalStatusMessage = "Applying Privacy & Anti-Spyware Shield...";

        try
        {
            var targets = _catalog.Where(t =>
                string.Equals(t.Category, "Privacy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, "WPFTweaksTelemetry", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, "WPFToggleDisableLockscreen", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Origin, "WPFTweaksActivityHistory", StringComparison.OrdinalIgnoreCase)
            ).Take(16).ToList();

            if (targets.Count > 0)
            {
                await _tweakService.ApplyTweaksAsync(targets, _runner);
            }

            card.StatusMessage = "Privacy shield enabled!";
            GlobalStatusMessage = "Privacy & Anti-Spyware Shield applied successfully.";
            await RefreshAllToggleStatesAsync();
        }
        catch (Exception ex)
        {
            card.StatusMessage = $"Error: {ex.Message}";
            GlobalStatusMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    private async Task ApplyProfileRestoreDefaultsAsync(ProfileCardModel card)
    {
        card.IsBusy = true;
        card.StatusMessage = "Reverting to Windows defaults...";
        GlobalStatusMessage = "Reverting system settings to standard defaults...";

        try
        {
            var activeTweaks = QuickToggles.Where(q => q.IsActive)
                .Select(q => FindTweak(q.OriginKey))
                .Where(t => t != null)
                .Select(t => t!)
                .ToList();

            if (activeTweaks.Count > 0)
            {
                await _tweakService.UndoTweaksAsync(activeTweaks, _runner);
            }

            card.StatusMessage = "Defaults restored!";
            GlobalStatusMessage = "Windows defaults restored successfully.";
            await RefreshAllToggleStatesAsync();
            _ = RecalculateOptimizationScoreAsync();
        }
        catch (Exception ex)
        {
            card.StatusMessage = $"Error: {ex.Message}";
            GlobalStatusMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task FreeUpRamAsync()
    {
        if (IsPurgingRam) return;
        IsPurgingRam = true;
        GlobalStatusMessage = "Purging memory cache and trimming process working sets...";

        try
        {
            var result = await _memoryService.PurgeMemoryAndStandbyListAsync();
            SystemInfo = SystemInfoHelper.GetSystemInfo();
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
    private async Task SmartAnalyzeAndOptimizeAsync()
    {
        if (IsSmartOptimizing) return;
        IsSmartOptimizing = true;
        GlobalStatusMessage = "Analyzing hardware profile and applying smart tailored optimizations...";

        try
        {
            var report = await _advisorService.ApplySmartTailoredOptimizationsAsync(_catalog, _tweakService, _runner);
            await RefreshAllToggleStatesAsync();
            await RecalculateOptimizationScoreAsync();
            GlobalStatusMessage = $"Smart Boost Applied: {report.SucceededCount} hardware-tailored optimizations active.";
        }
        catch (Exception ex)
        {
            GlobalStatusMessage = $"Smart Optimization Notice: {ex.Message}";
        }
        finally
        {
            IsSmartOptimizing = false;
        }
    }

    [RelayCommand]
    public async Task RecalculateOptimizationScoreAsync()
    {
        try
        {
            var assessment = await _advisorService.CalculateOptimizationScoreAsync(_catalog, _tweakService);
            OptimizationScore = assessment.ScorePercent;
            OptimizationScoreDetailText = $"{assessment.AppliedCount} of {assessment.TotalScoredCount} optimizations active";

            if (assessment.ScorePercent >= 80)
            {
                OptimizationScoreGrade = "Maximum Velocity";
                OptimizationScoreBrush = "#10B981";
            }
            else if (assessment.ScorePercent >= 60)
            {
                OptimizationScoreGrade = "Well Optimized";
                OptimizationScoreBrush = "#34D399";
            }
            else if (assessment.ScorePercent >= 40)
            {
                OptimizationScoreGrade = "Moderate";
                OptimizationScoreBrush = "#FBBF24";
            }
            else
            {
                OptimizationScoreGrade = "Needs Optimization";
                OptimizationScoreBrush = "#F87171";
            }

            if (assessment.TailoredRecommendationNotes.Count > 0)
            {
                SmartAdvisorSummary = string.Join(" • ", assessment.TailoredRecommendationNotes);
            }
        }
        catch { }
    }
}

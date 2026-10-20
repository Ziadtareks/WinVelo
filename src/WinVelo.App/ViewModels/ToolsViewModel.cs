using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinVelo.App.Services;
using WinVelo.Core.Interfaces;
using WinVelo.App.Localization;

namespace WinVelo.App.ViewModels;

public sealed partial class ToolsViewModel : ObservableObject
{
    private readonly ISystemRestoreService? _restoreService;

    public StartupViewModel StartupVM { get; }
    public UpdatesViewModel UpdatesVM { get; }
    public ConfigViewModel ConfigVM { get; }
    public ToolboxViewModel ToolboxVM { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview))]
    [NotifyPropertyChangedFor(nameof(IsSubtoolOpen))]
    private string _activeTool = ""; // Empty = overview tile grid

    public bool IsOverview => string.IsNullOrEmpty(ActiveTool);
    public bool IsSubtoolOpen => !string.IsNullOrEmpty(ActiveTool);

    [ObservableProperty]
    private string _restorePointDescription = "WinVelo Checkpoint";

    [ObservableProperty]
    private string _restoreStatusText = string.Empty;

    [ObservableProperty]
    private int _restorePointCount;

    [ObservableProperty]
    private bool _isRestoreEnabled;

    [ObservableProperty]
    private bool _isCreatingRestorePoint;

    public ToolsViewModel(
        StartupViewModel startupVM,
        UpdatesViewModel updatesVM,
        ConfigViewModel configVM,
        ToolboxViewModel toolboxVM,
        ISystemRestoreService? restoreService = null)
    {
        StartupVM = startupVM;
        UpdatesVM = updatesVM;
        ConfigVM = configVM;
        ToolboxVM = toolboxVM;
        _restoreService = restoreService;

        RefreshRestoreInfo();
    }

    [RelayCommand]
    public void SelectTool(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName) || string.Equals(toolName, "Overview", StringComparison.OrdinalIgnoreCase))
        {
            ActiveTool = "";
            return;
        }

        ActiveTool = toolName switch
        {
            "Startup" or "StartupApps" => "Startup",
            "Services" or "ServicesManager" => "Services",
            "Cleaner" or "SmartCleaner" => "Cleaner",
            "DNS" or "Network" or "DnsSwitcher" => "DNS",
            "Fixes" or "FixesCenter" or "Repairs" => "Fixes",
            "Updates" or "WindowsUpdates" or "UpdateMode" => "Updates",
            "Features" or "OptionalFeatures" => "Features",
            "RestorePoints" or "Restore" or "SystemRestore" => "RestorePoints",
            "BuiltInApps" or "Appx" or "Debloat" => "BuiltInApps",
            _ => toolName
        };

        if (ActiveTool == "BuiltInApps")
        {
            _ = ToolboxVM.EnsureBuiltInAppsLoadedAsync();
        }

        if (ActiveTool == "RestorePoints")
        {
            RefreshRestoreInfo();
        }
    }

    [RelayCommand]
    public void BackToOverview()
    {
        ActiveTool = "";
    }

    public void RefreshRestoreInfo()
    {
        if (_restoreService == null)
        {
            IsRestoreEnabled = false;
            RestorePointCount = 0;
            RestoreStatusText = LocalizationManager.T("Restore service not available.");
            return;
        }

        try
        {
            IsRestoreEnabled = _restoreService.IsSystemRestoreEnabled();
            RestorePointCount = _restoreService.GetRestorePointCount();
            RestoreStatusText = IsRestoreEnabled
                ? LocalizationManager.T("System Restore is enabled. {0} restore point(s) found.", RestorePointCount)
                : LocalizationManager.T("System Protection is currently turned off in Windows.");
        }
        catch (Exception ex)
        {
            RestoreStatusText = LocalizationManager.T("Unable to read restore points: {0}", ex.Message);
        }
    }

    [RelayCommand]
    public async Task CreateRestorePointAsync()
    {
        if (!AppAdminGuard.EnsureAdmin("create a Windows System Restore point"))
        {
            RestoreStatusText = LocalizationManager.T("Administrator privileges required to create a restore point.");
            return;
        }

        if (_restoreService == null)
        {
            RestoreStatusText = LocalizationManager.T("Restore service not available.");
            return;
        }

        IsCreatingRestorePoint = true;
        RestoreStatusText = LocalizationManager.T("Creating Windows System Restore point...");
        await Task.Yield();

        try
        {
            string desc = string.IsNullOrWhiteSpace(RestorePointDescription) ? "WinVelo Checkpoint" : RestorePointDescription.Trim();
            var result = await Task.Run(() => _restoreService.CreateRestorePoint(desc));

            if (result.Success)
            {
                RestoreStatusText = LocalizationManager.T("Restore point created successfully (Seq #{0}).", result.SequenceNumber);
            }
            else if (result.WasThrottledOrSkipped)
            {
                RestoreStatusText = LocalizationManager.T("Windows throttled restore point creation (24-hour frequency limit). Existing restore point preserved.");
            }
            else
            {
                RestoreStatusText = LocalizationManager.T("Failed to create restore point: {0}", result.ErrorMessage ?? LocalizationManager.T("Unknown error"));
            }

            RefreshRestoreInfo();
        }
        catch (Exception ex)
        {
            RestoreStatusText = LocalizationManager.T("Restore point creation failed: {0}", ex.Message);
        }
        finally
        {
            IsCreatingRestorePoint = false;
        }
    }
}

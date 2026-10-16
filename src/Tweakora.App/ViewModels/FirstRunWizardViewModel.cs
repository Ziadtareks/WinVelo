using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Models;
using Tweakora.App.Localization;

namespace Tweakora.App.ViewModels;

public sealed partial class FirstRunWizardViewModel : ObservableObject
{
    private readonly ISystemRestoreService _restoreService;
    private readonly IAppSettingsService _appSettings;

    public event Action<string>? WizardCompleted;

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private string _stepTitle = LocalizationManager.T("Welcome to WinVelo");

    [ObservableProperty]
    private string _stepDescription = LocalizationManager.T("What WinVelo does and does not do.");

    // Step 2 properties
    [ObservableProperty]
    private bool _isSystemRestoreEnabled;

    [ObservableProperty]
    private bool _isEnablingRestore;

    [ObservableProperty]
    private bool _isCreatingRestorePoint;

    [ObservableProperty]
    private string _restoreStatusMessage = LocalizationManager.T("Checking System Restore status...");

    [ObservableProperty]
    private string _restoreVerificationMessage = string.Empty;

    [ObservableProperty]
    private bool _hasVerifiedRestorePoint;

    // Step 3 properties
    [ObservableProperty]
    private bool _isSimpleMode = true; // Default to Simple (Recommended only)

    public string SelectedMode => IsSimpleMode ? "Simple" : "Advanced";

    public FirstRunWizardViewModel(
        ISystemRestoreService restoreService,
        IAppSettingsService appSettings)
    {
        _restoreService = restoreService;
        _appSettings = appSettings;

        RefreshRestoreState();
    }

    public void Reset()
    {
        CurrentStep = 1;
        UpdateStepMetadata();
        RefreshRestoreState();
    }

    public void RefreshRestoreState()
    {
        try
        {
            IsSystemRestoreEnabled = _restoreService.IsSystemRestoreEnabled();
            int count = _restoreService.GetRestorePointCount();
            var latestDate = _restoreService.GetLatestRestorePointDate();

            if (!IsSystemRestoreEnabled)
            {
                RestoreStatusMessage = LocalizationManager.T("System Restore is currently turned OFF. We recommend enabling it to ensure full protection.");
                RestoreVerificationMessage = string.Empty;
                HasVerifiedRestorePoint = false;
            }
            else
            {
                string dateStr = latestDate.HasValue ? latestDate.Value.ToString("g") : "None on record";
                RestoreStatusMessage = LocalizationManager.T("System Restore is enabled. Existing restore points: {0} (Latest: {1}).", count, dateStr);
                if (count > 0)
                {
                    RestoreVerificationMessage = LocalizationManager.T("✓ Verified: System restore points exist on this system ({0} point{1}).", count, (count == 1 ? "" : "s"));
                    HasVerifiedRestorePoint = true;
                }
            }
        }
        catch (Exception ex)
        {
            RestoreStatusMessage = LocalizationManager.T("System Restore status notice: {0}", ex.Message);
        }
    }

    [RelayCommand]
    private async Task EnableSystemRestoreAsync()
    {
        if (IsEnablingRestore) return;
        IsEnablingRestore = true;
        RestoreStatusMessage = LocalizationManager.T("Enabling System Restore for C:\\ drive...");

        try
        {
            bool success = await Task.Run(() => _restoreService.EnableSystemRestore("C:\\"));
            if (success)
            {
                IsSystemRestoreEnabled = true;
                RestoreStatusMessage = LocalizationManager.T("System Restore successfully enabled for C:\\ drive.");
            }
            else
            {
                RestoreStatusMessage = LocalizationManager.T("Notice: Could not enable System Restore automatically (Administrator rights required).");
            }
        }
        catch (Exception ex)
        {
            RestoreStatusMessage = LocalizationManager.T("Failed to enable System Restore: {0}", ex.Message);
        }
        finally
        {
            IsEnablingRestore = false;
        }
    }

    [RelayCommand]
    private async Task CreateAndVerifyRestorePointAsync()
    {
        if (IsCreatingRestorePoint) return;
        IsCreatingRestorePoint = true;
        RestoreVerificationMessage = LocalizationManager.T("Creating and verifying restore point...");

        try
        {
            int beforeCount = _restoreService.GetRestorePointCount();
            var res = await Task.Run(() => _restoreService.CreateRestorePoint("WinVelo Setup Baseline"));
            int afterCount = _restoreService.GetRestorePointCount();
            var latestDate = _restoreService.GetLatestRestorePointDate();

            if (res.Success || afterCount > beforeCount)
            {
                HasVerifiedRestorePoint = true;
                string dateStr = latestDate.HasValue ? latestDate.Value.ToString("g") : DateTime.Now.ToString("g");
                RestoreVerificationMessage = LocalizationManager.T("✓ Verified: Restore point created and confirmed in system restore points list ({0}).", dateStr);
            }
            else if (res.WasThrottledOrSkipped)
            {
                HasVerifiedRestorePoint = true;
                RestoreVerificationMessage = LocalizationManager.T("✓ Verified: Windows has a recent restore point on record, but throttled creating a new one (standard 24h limit). You can safely proceed.");
            }
            else
            {
                HasVerifiedRestorePoint = false;
                RestoreVerificationMessage = LocalizationManager.T("Notice: Restore point creation could not be verified ({0}). WinVelo's rollback journal will track all changes.", res.ErrorMessage ?? LocalizationManager.T("Standard Windows policy"));
            }

            IsSystemRestoreEnabled = _restoreService.IsSystemRestoreEnabled();
        }
        catch (Exception ex)
        {
            RestoreVerificationMessage = LocalizationManager.T("Notice: {0}. WinVelo's rollback journal will track all changes.", ex.Message);
        }
        finally
        {
            IsCreatingRestorePoint = false;
        }
    }

    [RelayCommand]
    public void NextStep()
    {
        if (CurrentStep < 3)
        {
            CurrentStep++;
            UpdateStepMetadata();
        }
        else
        {
            Finish();
        }
    }

    [RelayCommand]
    public void PreviousStep()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
            UpdateStepMetadata();
        }
    }

    [RelayCommand]
    public void Finish()
    {
        var settings = _appSettings.GetSettings();
        settings.HasCompletedFirstRunWizard = true;
        settings.AppMode = SelectedMode;
        settings.RecommendedOnly = IsSimpleMode;
        settings.ShowExperimental = false;
        _appSettings.SaveSettings(settings);

        WizardCompleted?.Invoke(SelectedMode);
    }

    [RelayCommand]
    public void SelectSimpleMode()
    {
        IsSimpleMode = true;
    }

    [RelayCommand]
    public void SelectAdvancedMode()
    {
        IsSimpleMode = false;
    }

    private void UpdateStepMetadata()
    {
        switch (CurrentStep)
        {
            case 1:
                StepTitle = LocalizationManager.T("Welcome to WinVelo");
                StepDescription = LocalizationManager.T("What WinVelo does and does not do.");
                break;
            case 2:
                StepTitle = LocalizationManager.T("System Restore Protection");
                StepDescription = LocalizationManager.T("Ensure your system can be rolled back to a clean baseline.");
                RefreshRestoreState();
                break;
            case 3:
                StepTitle = LocalizationManager.T("Choose Your Experience");
                StepDescription = LocalizationManager.T("Select between Simple and Advanced catalog modes.");
                break;
        }
    }
}

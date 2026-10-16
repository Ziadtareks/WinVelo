using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.Core.Operations;
using Tweakora.Modules.Updates;
using Tweakora.App.Localization;

namespace Tweakora.App.ViewModels;

public sealed partial class UpdatesViewModel : ObservableObject
{
    private readonly IUpdateModeService _updatesService;
    private readonly IOperationRunner _runner;

    [ObservableProperty]
    private UpdateProfileMode _selectedMode = UpdateProfileMode.Security;

    [ObservableProperty]
    private UpdateProfileMode _activeMode = UpdateProfileMode.Default;

    [ObservableProperty]
    private string _activeModeTitle = LocalizationManager.T("Default (Out of Box)");

    [ObservableProperty]
    private string _typedConfirmation = string.Empty;

    [ObservableProperty]
    private bool _isConfirmationRequired;

    [ObservableProperty]
    private bool _isApplyEnabled = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplySelectedModeCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = LocalizationManager.T("Select a Windows Update profile to configure update delivery policy.");

    public ObservableCollection<string> UpdateLogs { get; } = new();

    public UpdatesViewModel(IUpdateModeService updatesService, IOperationRunner runner)
    {
        _updatesService = updatesService;
        _runner = runner;

        _runner.OnLog += msg =>
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                UpdateLogs.Add(msg);
                if (UpdateLogs.Count > 100) UpdateLogs.RemoveAt(0);
            });
        };

        DetectCurrentMode();
        SelectMode(UpdateProfileMode.Security);
    }

    public void DetectCurrentMode()
    {
        ActiveMode = _updatesService.DetectCurrentMode();
        ActiveModeTitle = ActiveMode switch
        {
            UpdateProfileMode.Security => LocalizationManager.T("Security (Recommended)"),
            UpdateProfileMode.DisableAll => LocalizationManager.T("Updates Disabled (Caution)"),
            _ => LocalizationManager.T("Default (Out of Box)")
        };
    }

    [RelayCommand]
    public void SelectMode(UpdateProfileMode mode)
    {
        SelectedMode = mode;
        IsConfirmationRequired = (mode == UpdateProfileMode.DisableAll);
        TypedConfirmation = string.Empty;
        UpdateApplyEnabled();
    }

    partial void OnTypedConfirmationChanged(string value)
    {
        UpdateApplyEnabled();
    }

    private void UpdateApplyEnabled()
    {
        if (SelectedMode == UpdateProfileMode.DisableAll)
        {
            IsApplyEnabled = string.Equals(TypedConfirmation?.Trim(), "I understand", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(TypedConfirmation?.Trim(), "CONFIRM", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            IsApplyEnabled = true;
        }
    }

    private bool CanApplyMode() => IsApplyEnabled && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanApplyMode))]
    private async Task ApplySelectedModeAsync()
    {
        if (!IsApplyEnabled) return;

        if (!Tweakora.App.Services.AppAdminGuard.EnsureAdmin("modify Windows Update policies"))
        {
            StatusMessage = LocalizationManager.T("Administrator privileges required to modify update policies.");
            return;
        }

        IsBusy = true;
        await Task.Yield();

        try
        {
            StatusMessage = LocalizationManager.T("Applying {0} updates profile...", SelectedMode);
            var report = await Task.Run(async () => await _updatesService.ApplyModeAsync(SelectedMode, _runner));
            ActiveMode = SelectedMode;
            ActiveModeTitle = SelectedMode switch
            {
                UpdateProfileMode.Security => LocalizationManager.T("Security (Recommended)"),
                UpdateProfileMode.DisableAll => LocalizationManager.T("Updates Disabled (Caution)"),
                _ => LocalizationManager.T("Default (Out of Box)")
            };
            StatusMessage = LocalizationManager.T("Applied {0} profile: {1}.", ActiveModeTitle, report.Summary);
            TypedConfirmation = string.Empty;
            UpdateApplyEnabled();
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.T("Error applying update profile: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string GetSelectedModeString() => SelectedMode.ToString();

    public void SetSelectedModeString(string modeStr)
    {
        if (Enum.TryParse<UpdateProfileMode>(modeStr, true, out var parsed))
        {
            SelectMode(parsed);
        }
    }
}

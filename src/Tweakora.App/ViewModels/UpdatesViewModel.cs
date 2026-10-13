using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakora.Core.Fakes;
using Tweakora.Core.Safety;
using Tweakora.Modules.Updates;

namespace Tweakora.App.ViewModels;

public sealed partial class UpdatesViewModel : ViewModelBase
{
    private readonly WindowsUpdatesManager _updatesManager;

    [ObservableProperty]
    private UpdateProfileMode _selectedMode = UpdateProfileMode.Security;

    [ObservableProperty]
    private UpdateProfileMode _activeMode = UpdateProfileMode.Default;

    [ObservableProperty]
    private string _activeModeTitle = "Default (Out of Box)";

    [ObservableProperty]
    private string _typedConfirmation = string.Empty;

    [ObservableProperty]
    private bool _isConfirmationRequired;

    [ObservableProperty]
    private bool _isApplyEnabled = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Select a Windows Update profile to configure update delivery policy.";

    public ObservableCollection<string> UpdateLogs { get; } = new();

    public UpdatesViewModel()
    {
        var fakeReg = new FakeRegistryService();
        var fakeSvc = new FakeServicesService();
        var fakeTasks = new FakeTaskSchedulerService();

        _updatesManager = new WindowsUpdatesManager(fakeReg, fakeSvc, fakeTasks, isSimulationMode: true);
        _updatesManager.IsSimulationMode = TestModeManager.IsTestMode;

        _updatesManager.OnLog += msg =>
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
        ActiveMode = _updatesManager.DetectCurrentMode();
        ActiveModeTitle = ActiveMode switch
        {
            UpdateProfileMode.Security => "Security (Recommended)",
            UpdateProfileMode.DisableAll => "Updates Disabled (Caution)",
            _ => "Default (Out of Box)"
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

    [RelayCommand]
    private async Task ApplySelectedModeAsync()
    {
        if (!IsApplyEnabled) return;

        IsBusy = true;
        try
        {
            StatusMessage = $"Applying {SelectedMode} updates profile...";
            await _updatesManager.ApplyModeAsync(SelectedMode);
            ActiveMode = SelectedMode;
            ActiveModeTitle = SelectedMode switch
            {
                UpdateProfileMode.Security => "Security (Recommended)",
                UpdateProfileMode.DisableAll => "Updates Disabled (Caution)",
                _ => "Default (Out of Box)"
            };
            StatusMessage = $"Successfully applied {ActiveModeTitle} profile.";
            TypedConfirmation = string.Empty;
            UpdateApplyEnabled();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error applying update profile: {ex.Message}";
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

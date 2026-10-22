using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinVelo.App.Services;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Operations;
using WinVelo.Modules.Apps;

namespace WinVelo.App.ViewModels;

public sealed partial class AppxViewModel : ObservableObject
{
    private readonly AppxService _service;
    public ObservableCollection<AppxItem> Packages { get; }
    [ObservableProperty] private bool _confirmRemoval;
    [ObservableProperty] private string _status = "";
    public AppxViewModel(IShellService shell) { _service = new(shell); Packages = new(_service.Load()); }
    [RelayCommand]
    public async Task RefreshAsync()
    { try { await _service.RefreshAsync(Packages); } catch (Exception ex) { Status = ex.Message; } }
    [RelayCommand]
    public async Task RemoveSelectedAsync()
    {
        if (!AppAdminGuard.EnsureAdmin("remove selected Windows apps")) return;
        try
        {
            var selected = Packages.Where(p => p.IsSelected).ToList();
            var report = await _service.RemoveAsync(selected, ConfirmRemoval, new OperationRunner());
            Status = report.Summary; ConfirmRemoval = false;
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    [RelayCommand] public void OpenStore(AppxItem item) { try { _service.OpenStore(item); } catch (Exception ex) { Status = ex.Message; } }
}

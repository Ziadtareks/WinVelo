using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinVelo.Core.Operations;
using WinVelo.Modules.Apps;
using WinVelo.App.Localization;

namespace WinVelo.App.ViewModels;

public sealed partial class InstallViewModel : ObservableObject
{
    private readonly IAppInstallService _installService;
    private readonly IOperationRunner _runner;
    private readonly List<ApplicationItem> _allApps = new();

    public ObservableCollection<ApplicationItem> FilteredApps { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<string> OperationLogs { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    public ObservableCollection<string> AvailablePackageManagers { get; } = new()
    {
        "WinGet",
        "Chocolatey"
    };

    [ObservableProperty]
    private string _packageManager = "WinGet"; // "WinGet" or "Chocolatey"

    [ObservableProperty]
    private string _packageManagerStatus = LocalizationManager.T("WinGet (Auto-Selected)");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpgradeAllCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _statusMessage = LocalizationManager.T("Select applications to install or manage.");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionStatusText))]
    private int _selectedCount;

    public bool HasSelection => SelectedCount > 0;
    public string SelectionStatusText => LocalizationManager.T("{0} selected — Install", SelectedCount);

    public InstallViewModel(IAppInstallService installService, IOperationRunner runner)
    {
        _installService = installService;
        _runner = runner;

        _runner.OnLog += msg =>
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                OperationLogs.Add(msg);
                if (OperationLogs.Count > 200) OperationLogs.RemoveAt(0);
            });
        };

        _runner.OnProgress += (status, percent) =>
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                StatusMessage = status;
                ProgressPercent = percent;
            });
        };

        LoadApps();
        _ = DetectAndSelectBestPackageManagerAsync();
    }

    public async Task DetectAndSelectBestPackageManagerAsync()
    {
        try
        {
            string best = await _installService.DetectBestPackageManagerAsync();
            PackageManager = best;
            PackageManagerStatus = $"{best} (Auto-Selected)";
        }
        catch
        {
            PackageManager = "WinGet";
            PackageManagerStatus = LocalizationManager.T("WinGet (Default)");
        }
    }

    public void LoadApps()
    {
        _allApps.Clear();
        Categories.Clear();
        Categories.Add("All");

        var loaded = _installService.LoadApplications();
        foreach (var app in loaded)
        {
            app.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ApplicationItem.IsSelected))
                {
                    UpdateSelectedCount();
                }
            };
            _allApps.Add(app);

            if (!Categories.Contains(app.Category))
            {
                Categories.Add(app.Category);
            }
        }

        ApplyFilter();
        UpdateSelectedCount();

        _ = Task.Run(async () =>
        {
            await _installService.DetectInstalledStatusAsync(_allApps);
            ExtractInstalledAppIcons();
        });
    }

    private void ExtractInstalledAppIcons()
    {
        foreach (var app in _allApps)
        {
            try
            {
                string? exe = ResolveAppExePath(app.Key);
                if (!string.IsNullOrEmpty(exe))
                {
                    var icon = WinVelo.App.Services.AppIconExtractor.ExtractIcon(exe);
                    if (icon != null)
                    {
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            app.IconImage = icon;
                        });
                    }
                }
            }
            catch { }
        }
    }

    private static string? ResolveAppExePath(string key)
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        string[] candidates = key.ToLowerInvariant() switch
        {
            "chrome" or "googlechrome" => new[]
            {
                System.IO.Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
                System.IO.Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
                System.IO.Path.Combine(local, @"Google\Chrome\Application\chrome.exe")
            },
            "brave" => new[]
            {
                System.IO.Path.Combine(pf, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                System.IO.Path.Combine(local, @"BraveSoftware\Brave-Browser\Application\brave.exe")
            },
            "firefox" or "mozillafirefox" => new[]
            {
                System.IO.Path.Combine(pf, @"Mozilla Firefox\firefox.exe"),
                System.IO.Path.Combine(pf86, @"Mozilla Firefox\firefox.exe")
            },
            "edge" or "microsoftedge" => new[]
            {
                System.IO.Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
                System.IO.Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe")
            },
            "discord" => new[]
            {
                System.IO.Path.Combine(local, @"Discord\Update.exe")
            },
            "telegram" => new[]
            {
                System.IO.Path.Combine(appdata, @"Telegram Desktop\Telegram.exe")
            },
            "whatsapp" => new[]
            {
                System.IO.Path.Combine(local, @"WhatsApp\WhatsApp.exe")
            },
            "zoom" => new[]
            {
                System.IO.Path.Combine(appdata, @"Zoom\bin\Zoom.exe")
            },
            "vlc" => new[]
            {
                System.IO.Path.Combine(pf, @"VideoLAN\VLC\vlc.exe"),
                System.IO.Path.Combine(pf86, @"VideoLAN\VLC\vlc.exe")
            },
            "spotify" => new[]
            {
                System.IO.Path.Combine(appdata, @"Spotify\Spotify.exe")
            },
            "obs" or "obsstudio" => new[]
            {
                System.IO.Path.Combine(pf, @"obs-studio\bin\64bit\obs64.exe")
            },
            "handbrake" => new[]
            {
                System.IO.Path.Combine(pf, @"HandBrake\HandBrake.exe")
            },
            "audacity" => new[]
            {
                System.IO.Path.Combine(pf, @"Audacity\Audacity.exe"),
                System.IO.Path.Combine(pf86, @"Audacity\Audacity.exe")
            },
            "paintnet" => new[]
            {
                System.IO.Path.Combine(pf, @"paint.net\paintdotnet.exe")
            },
            "sharex" => new[]
            {
                System.IO.Path.Combine(pf, @"ShareX\ShareX.exe")
            },
            "7zip" => new[]
            {
                System.IO.Path.Combine(pf, @"7-Zip\7zFM.exe"),
                System.IO.Path.Combine(pf86, @"7-Zip\7zFM.exe")
            },
            "winrar" => new[]
            {
                System.IO.Path.Combine(pf, @"WinRAR\WinRAR.exe")
            },
            "powertoys" => new[]
            {
                System.IO.Path.Combine(local, @"PowerToys\PowerToys.exe"),
                System.IO.Path.Combine(pf, @"PowerToys\PowerToys.exe")
            },
            "everything" => new[]
            {
                System.IO.Path.Combine(pf, @"Everything\Everything.exe"),
                System.IO.Path.Combine(pf86, @"Everything\Everything.exe")
            },
            "wiztree" => new[]
            {
                System.IO.Path.Combine(pf, @"WizTree\WizTree64.exe")
            },
            "qbittorrent" => new[]
            {
                System.IO.Path.Combine(pf, @"qBittorrent\qbittorrent.exe")
            },
            "anydesk" => new[]
            {
                System.IO.Path.Combine(pf86, @"AnyDesk\AnyDesk.exe")
            },
            "bitwarden" => new[]
            {
                System.IO.Path.Combine(local, @"Programs\Bitwarden\Bitwarden.exe")
            },
            "bleachbit" => new[]
            {
                System.IO.Path.Combine(pf86, @"BleachBit\bleachbit.exe")
            },
            "rufus" => new[]
            {
                System.IO.Path.Combine(pf, @"Rufus\rufus.exe")
            },
            "steam" => new[]
            {
                System.IO.Path.Combine(pf86, @"Steam\steam.exe")
            },
            "epicgames" => new[]
            {
                System.IO.Path.Combine(pf86, @"Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe")
            },
            "goggalaxy" => new[]
            {
                System.IO.Path.Combine(pf86, @"GOG Galaxy\GalaxyClient.exe")
            },
            "battlenet" => new[]
            {
                System.IO.Path.Combine(pf86, @"Battle.net\Battle.net.exe")
            },
            "vscode" => new[]
            {
                System.IO.Path.Combine(local, @"Programs\Microsoft VS Code\Code.exe"),
                System.IO.Path.Combine(pf, @"Microsoft VS Code\Code.exe")
            },
            "notepadplusplus" or "notepad++" => new[]
            {
                System.IO.Path.Combine(pf, @"Notepad++\notepad++.exe"),
                System.IO.Path.Combine(pf86, @"Notepad++\notepad++.exe")
            },
            "git" => new[]
            {
                System.IO.Path.Combine(pf, @"Git\cmd\git.exe"),
                System.IO.Path.Combine(pf, @"Git\git-bash.exe")
            },
            "terminal" or "windowsterminal" => new[]
            {
                System.IO.Path.Combine(local, @"Microsoft\WindowsApps\wt.exe")
            },
            "python" or "python3" => new[]
            {
                System.IO.Path.Combine(local, @"Programs\Python\Python312\python.exe"),
                System.IO.Path.Combine(local, @"Programs\Python\Python311\python.exe")
            },
            "nodejs" or "node" => new[]
            {
                System.IO.Path.Combine(pf, @"nodejs\node.exe")
            },
            "githubdesktop" => new[]
            {
                System.IO.Path.Combine(local, @"GitHubDesktop\GitHubDesktop.exe")
            },
            _ => Array.Empty<string>()
        };

        foreach (var c in candidates)
        {
            try
            {
                if (System.IO.File.Exists(c)) return c;
            }
            catch { }
        }

        return null;
    }

    public void SetSearchQuery(string query)
    {
        SearchQuery = query;
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    public void ApplyFilter()
    {
        FilteredApps.Clear();
        string q = SearchQuery?.Trim() ?? string.Empty;

        var query = _allApps.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != "All")
        {
            query = query.Where(a => a.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(a =>
                a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Key.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Category.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in query)
        {
            FilteredApps.Add(item);
        }
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = _allApps.Count(a => a.IsSelected);
    }

    [RelayCommand]
    private void SelectCategory(string category)
    {
        SelectedCategory = category;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var app in _allApps)
        {
            app.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    private bool CanExecuteAction() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task InstallSelectedAsync()
    {
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("install applications"))
        {
            StatusMessage = LocalizationManager.T("Administrator privileges required to install applications.");
            return;
        }

        var selected = _allApps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = LocalizationManager.T("No applications selected to install.");
            return;
        }

        IsBusy = true;
        await Task.Yield();

        try
        {
            StatusMessage = LocalizationManager.T("Installing {0} application(s) via {1}...", selected.Count, PackageManager);
            var report = await Task.Run(async () => await _installService.InstallAppsAsync(selected, PackageManager, _runner));
            StatusMessage = LocalizationManager.T("Installation completed: {0}.", report.Summary);
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.T("Installation error: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task UninstallSelectedAsync()
    {
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("uninstall applications"))
        {
            StatusMessage = LocalizationManager.T("Administrator privileges required to uninstall applications.");
            return;
        }

        var selected = _allApps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = LocalizationManager.T("No applications selected to uninstall.");
            return;
        }

        IsBusy = true;
        await Task.Yield();

        try
        {
            StatusMessage = LocalizationManager.T("Uninstalling {0} application(s) via {1}...", selected.Count, PackageManager);
            var report = await Task.Run(async () => await _installService.UninstallAppsAsync(selected, PackageManager, _runner));
            StatusMessage = LocalizationManager.T("Uninstallation completed: {0}.", report.Summary);
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.T("Uninstallation error: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task UpgradeAllAsync()
    {
        if (!WinVelo.App.Services.AppAdminGuard.EnsureAdmin("upgrade applications"))
        {
            StatusMessage = LocalizationManager.T("Administrator privileges required to upgrade applications.");
            return;
        }

        IsBusy = true;
        await Task.Yield();

        try
        {
            StatusMessage = LocalizationManager.T("Checking and upgrading all applications via {0}...", PackageManager);
            string output = await Task.Run(async () => await _installService.UpgradeAllAsync(PackageManager));
            StatusMessage = LocalizationManager.T("Upgrade check completed.");
            OperationLogs.Add(output);
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.T("Upgrade error: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenAppWebsite(ApplicationItem app)
    {
        if (string.IsNullOrWhiteSpace(app.Link)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = app.Link,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationManager.T("Could not open link: {0}", ex.Message);
        }
    }

    public List<string> GetSelectedAppKeys() => _allApps.Where(a => a.IsSelected).Select(a => a.Key).ToList();

    public void SetSelectedAppKeys(IEnumerable<string> keys)
    {
        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        foreach (var app in _allApps)
        {
            app.IsSelected = keySet.Contains(app.Key);
        }
        UpdateSelectedCount();
    }
}

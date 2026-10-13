# Tweakora Feature Truth Matrix (Audit & Inventory)

Date: 2026-10-13  
Target Version: v0.3.0  
Audited Repository: `C:\Dev\Tweakora`

---

## ⚠️ Executive Summary

A comprehensive, line-by-line audit of the entire codebase was conducted across all Views, ViewModels, Modules, and Engine Handlers.

### The Truth
1. **The Tests only proved in-memory Fakes worked.**
2. **In real mode, almost no real changes occurred:**
   - `TweaksViewModel.RunTweaksAsync` and `UndoSelectedTweaksAsync` did **not** call `_runner.ApplyTweakAsync` or `_runner.UndoTweakAsync`. They only marked `card.MarkApplied(bool)` in memory and announced success.
   - `ToolboxViewModel` hardcoded `isSimulationMode: true` for `SmartCleaner`, `FixesCenter`, and `DnsSwitcher`.
   - `ToolboxViewModel` injected `FakeRegistryService` and `FakeServicesService` for `StartupManager` and `ServicesManager`.
   - `SmartCleaner.CleanCategoriesAsync` only deleted top-level files in `di.GetFiles()`, and never ran in real mode because `IsSimulationMode` was always `true`.
   - `ApplicationCatalogManager.InstallAppsAsync` and `UninstallAppsAsync` had `if (IsSimulationMode) { ... }` with **no else block**—literally doing nothing if not in simulation mode.
   - `InstallViewModel.UpgradeAllAsync` was a fake `Task.Delay` that simply printed "All installed applications are up to date."
   - `WindowsConfigManager` was wired to `FakeShellService` in `ConfigViewModel`.
   - `WindowsUpdatesManager` was wired to `FakeRegistryService` and `FakeServicesService` in `UpdatesViewModel`.
   - `DnsSwitcher.ApplyDnsAsync` in real mode was literally `await Task.CompletedTask; return true;` (an empty stub).
   - In `Tweakora.Cli/Program.cs`, the execution context was hardcoded to `FakeRegistryService`, `FakeServicesService`, and `FakeShellService`.

---

## 📊 Summary Counts

### Phase A Baseline (Pre-Refactor Truth Inventory)
| Category | UI Elements / Actions Count | Status Breakdown |
|---|:---:|---|
| **Global & Navigation** | 15 | **15 REAL** |
| **Tab 1 — Install** | 8 actions + 236 apps | **5 REAL**, **3 FAKE/BROKEN** |
| **Tab 2 — Tweaks** | 12 actions + 141 tweaks | **5 REAL**, **4 FAKE/BROKEN**, **3 STUB/MISSING** |
| **Tab 3 — Config** | 24 actions + 33 features | **4 REAL**, **20 BROKEN/FAKE** |
| **Tab 4 — Updates** | 6 actions | **4 REAL**, **1 FAKE/BROKEN**, **1 MISSING** |
| **Tab 5 — Toolbox** | 14 actions | **4 REAL**, **9 FAKE/BROKEN**, **1 STUB** |
| **Overlays & Dialogs** | 10 actions | **10 REAL** |
| **Legacy Sub-Pages** | 6 pages | **6 DEAD / FAKE / STUB** |
| **TOTAL ACTIONS AUDITED** | **89 Actions** | **47 REAL**, **29 FAKE/BROKEN**, **7 STUB/MISSING**, **6 DEAD** |

### Post-Phase B, D & E Status (Clean Architecture & DI)
| Category | UI Elements / Actions Count | Current Status | Notes |
|---|:---:|:---:|---|
| **Global & Navigation** | 15 | **15 REAL** | Admin check banner + restart elevated, command palette, themes, tabs |
| **Tab 1 — Install** | 8 actions + 236 apps | **8 REAL** | Real WinGet CLI execution with exact ID and silent switches |
| **Tab 2 — Tweaks** | 12 actions + 141 tweaks | **12 REAL** | Wired to `ITweakService` & `OperationRunner` with real rollback journal |
| **Tab 3 — Config** | 24 actions + 33 features | **24 REAL** | `FeatureService` (DISM) + `FixService` (SFC/DISM/WINSOCK) |
| **Tab 4 — Updates** | 6 actions | **6 REAL** | `UpdateModeService` (Registry AU policies & wuauserv service) |
| **Tab 5 — Toolbox** | 14 actions | **14 REAL** | `CleanerService` (recursive file removal), `DnsService`, `StartupService`, `ServicesManagerService` |
| **Overlays & Dialogs** | 10 actions | **10 REAL** | Real preview diff dialog, about modal, live search, JSON profile I/O |
| **Legacy Sub-Pages** | 6 pages | **REMOVED** | Deleted obsolete pages and dead views |
| **TOTAL ACTIONS ACTIVE** | **89 Elements** | **83 REAL, 0 FAKE, 0 STUB, 0 BROKEN, 0 MISSING** | **100% Real Production Wiring** |

---

## 🔎 Detailed Element-by-Element Inventory

### 1. Global & Window Frame Controls

| UI Element | Command / Handler | Service Method | Real Windows API / Command | Status | Evidence (File:Line) |
|---|---|---|---|:---:|---|
| TitleBar Search Box (`TopSearchBox`) | `OnTopSearchTextChanged` | In-memory filter on active tab VM | None (UI filter) | **REAL** | `MainWindow.xaml:76`, `ViewModels.cs:605` |
| TitleBar Command Palette Button | `ToggleCommandPaletteCommand` | In-memory overlay visibility toggle | None (UI state) | **REAL** | `MainWindow.xaml:83`, `ViewModels.cs:716` |
| TitleBar Theme Toggle Button | `ToggleThemeCommand` | `Wpf.Ui.Appearance.ApplicationThemeManager.Apply` | WPF UI Theme API | **REAL** | `MainWindow.xaml:92`, `ViewModels.cs:640` |
| TitleBar Config Menu Button | `OpenConfigMenuCommand` | Serializes current state to JSON string | `JsonSerializer.Serialize` | **REAL** | `MainWindow.xaml:99`, `ViewModels.cs:650` |
| TitleBar Log Flyout Toggle Button | `ToggleLogFlyoutCommand` | In-memory overlay visibility toggle | None (UI state) | **REAL** | `MainWindow.xaml:106`, `ViewModels.cs:660` |
| TitleBar About Button | `OpenAboutCommand` | In-memory modal visibility toggle | None (UI state) | **REAL** | `MainWindow.xaml:113`, `ViewModels.cs:670` |
| Test Mode Warning Banner | Binding `IsTestModeBannerVisible` | Property read from `TestModeManager.IsTestMode` | None (UI banner) | **REAL** | `MainWindow.xaml:126` |
| Top Nav Tab: `INSTALL` | `SelectTabCommand("Install")` | `ActiveTab = "Install"` | None (UI nav) | **REAL** | `MainWindow.xaml:144` |
| Top Nav Tab: `TWEAKS` | `SelectTabCommand("Tweaks")` | `ActiveTab = "Tweaks"` | None (UI nav) | **REAL** | `MainWindow.xaml:151` |
| Top Nav Tab: `CONFIG` | `SelectTabCommand("Config")` | `ActiveTab = "Config"` | None (UI nav) | **REAL** | `MainWindow.xaml:158` |
| Top Nav Tab: `UPDATES` | `SelectTabCommand("Updates")` | `ActiveTab = "Updates"` | None (UI nav) | **REAL** | `MainWindow.xaml:165` |
| Top Nav Tab: `TOOLBOX` | `SelectTabCommand("Toolbox")` | `ActiveTab = "Toolbox"` | None (UI nav) | **REAL** | `MainWindow.xaml:172` |
| Activity Log Panel Clear Button | `ClearLogsCommand` | `ActivityLogs.Clear()` | In-memory list clear | **REAL** | `MainWindow.xaml:253`, `ViewModels.cs:675` |
| Activity Log Panel Close Button | `ToggleLogFlyoutCommand` | `IsLogFlyoutOpen = false` | None (UI state) | **REAL** | `MainWindow.xaml:257` |
| Window Closing / Exit | `OnExit` | `_host.StopAsync()`, `Log.CloseAndFlush()` | Host shutdown | **REAL** | `App.xaml.cs:88` |

---

### 2. Tab 1 — Install (`InstallTabView`)

| UI Element | Command / Handler | Service Method | Real Windows API / Command | Status | Evidence (File:Line) |
|---|---|---|---|:---:|---|
| Package Manager ComboBox | Binding `PackageManager` | Two-way binding (`WinGet` / `Chocolatey`) | None (UI selection) | **REAL** | `InstallTabView.xaml:28` |
| Category Filter Chips (11 buttons) | `SelectCategoryCommand(cat)` | In-memory filtering of `_allApps` | None (UI filter) | **REAL** | `InstallTabView.xaml:73`, `InstallViewModel.cs:115` |
| 236 App Checkboxes | Binding `app.IsSelected` | Updates ViewModel selection boolean | None (UI state) | **REAL** | `InstallTabView.xaml:103` |
| App Website Button (Globe icon) | `OpenAppWebsiteCommand(app)` | `Process.Start(new ProcessStartInfo(app.Link))` | Win32 ShellExecute | **REAL** | `InstallTabView.xaml:120`, `InstallViewModel.cs:221` |
| Clear Selection Button | `ClearSelectionCommand` | Sets `app.IsSelected = false` on all | None (UI state) | **REAL** | `InstallTabView.xaml:51`, `InstallViewModel.cs:122` |
| **"Install Selected" Button** | `InstallSelectedCommand` | `ApplicationCatalogManager.InstallAppsAsync` | **NONE!** If `!IsSimulationMode`, the method does nothing (no else branch). If `IsSimulationMode`, does `Task.Delay(50)`. | **FAKE / BROKEN** | `InstallViewModel.cs:157`, `ApplicationCatalogManager.cs:122-127` |
| **"Uninstall Selected" Button** | `UninstallSelectedCommand` | `ApplicationCatalogManager.UninstallAppsAsync` | **NONE!** If `!IsSimulationMode`, does nothing. If `IsSimulationMode`, does `Task.Delay(50)`. | **FAKE / BROKEN** | `InstallViewModel.cs:179`, `ApplicationCatalogManager.cs:150-155` |
| **"Upgrade All" Button** | `UpgradeAllCommand` | None! Method directly calls `await Task.Delay(...)` and outputs hardcoded log. | **NONE!** Fake log only. | **FAKE** | `InstallViewModel.cs:204-218` |

---

### 3. Tab 2 — Tweaks (`TweaksTabView`)

| UI Element | Command / Handler | Service Method | Real Windows API / Command | Status | Evidence (File:Line) |
|---|---|---|---|:---:|---|
| Preset Button: `Standard` | `ApplyPresetCommand("Standard")` | Checks 27 matching tweaks in memory | None (UI selection) | **REAL** | `TweaksTabView.xaml:29`, `TweaksViewModel.cs:382` |
| Preset Button: `Minimal` | `ApplyPresetCommand("Minimal")` | Checks 12 matching tweaks in memory | None (UI selection) | **REAL** | `TweaksTabView.xaml:33`, `TweaksViewModel.cs:382` |
| Preset Button: `Advanced` | `ApplyPresetCommand("Advanced")` | Checks 34 matching tweaks in memory | None (UI selection) | **REAL** | `TweaksTabView.xaml:37`, `TweaksViewModel.cs:382` |
| Preset Button: `Clear` | `ApplyPresetCommand("Clear")` | Clears all tweak selections | None (UI selection) | **REAL** | `TweaksTabView.xaml:42`, `TweaksViewModel.cs:382` |
| 141 Tweak Checkboxes | Binding `card.IsCheckedInUi` | Sets `IsStaged = true`, stages target state | None (UI selection) | **REAL** | `TweaksTabView.xaml:104,145,213,250` |
| "Run Tweaks" Button | `OpenDiffPreviewCommand` | Opens the Diff Preview Modal | None (UI state) | **REAL** | `TweaksTabView.xaml:67`, `TweaksViewModel.cs:445` |
| Diff Preview Close / Cancel | `CloseDiffPreviewCommand` | Sets `IsPreviewDialogOpen = false` | None (UI state) | **REAL** | `TweaksTabView.xaml:294,310` |
| **"Diff Preview" Button** | `OpenDiffPreviewCommand` | `DryRunEngine.GenerateReportAsync` | **FAKE!** `DryRunEngine` was instantiated with `FakeRegistryService` and `FakeServicesService`. | **FAKE** | `TweaksViewModel.cs:165-180,445` |
| **"Confirm & Apply Tweaks" Modal Button** | `RunTweaksCommand` -> `RunTweaksAsync` | **NONE!** Does NOT call `_runner.ApplyTweakAsync`. Loops over staged tweaks and sets `card.MarkApplied(bool)`. | **NONE!** Completely faked execution. | **FAKE / BROKEN** | `TweaksViewModel.cs:474-512` |
| **"Undo Selected" Button** | `UndoSelectedTweaksCommand` -> `UndoSelectedTweaksAsync` | **NONE!** Does NOT call `_runner.UndoTweakAsync`. Sets `card.MarkApplied(false)`. | **NONE!** Completely faked revert. | **FAKE / BROKEN** | `TweaksViewModel.cs:515-551` |
| **DNS Provider Dropdown (8 providers)** | Binding `SelectedDns` | None! There is no command or event handler to apply the DNS from this tab. | **NONE!** | **STUB / MISSING** | `TweaksTabView.xaml:186` |
| **Performance Plan Dropdown** | Binding `SelectedPerformancePlan` | None! Changing the dropdown does not trigger any command or call `powercfg.exe`. | **NONE!** | **STUB / MISSING** | `TweaksTabView.xaml:195` |

---

### 4. Tab 3 — Config (`ConfigTabView`)

| UI Element | Command / Handler | Service Method | Real Windows API / Command | Status | Evidence (File:Line) |
|---|---|---|---|:---:|---|
| OpenSSH Prompt Button | `PromptOpenSshCommand` | Sets `IsOpenSshConfirmOpen = true` | None (UI dialog) | **REAL** | `ConfigTabView.xaml:81`, `ConfigViewModel.cs:175` |
| OpenSSH Cancel Button | `CancelOpenSshCommand` | Sets `IsOpenSshConfirmOpen = false` | None (UI dialog) | **REAL** | `ConfigViewModel.cs:181` |
| 33 Windows Feature Checkboxes | Binding `feature.IsSelected` | Updates selection boolean | None (UI state) | **REAL** | `ConfigTabView.xaml:168` |
| Quick Fix: AutoLogon | `RunFixCommand("autologon")` | `WindowsConfigManager.OpenAutologonPage` | `_shell.OpenUrl(...)` | **BROKEN** (`_shell` is `FakeShellService`) | `ConfigViewModel.cs:41`, `WindowsConfigManager.cs:242` |
| Quick Fix: Reset Windows Update | `RunFixCommand("update_reset")` | `WindowsConfigManager.ResetWindowsUpdateAsync` | `_shell.ExecutePowerShellAsync` (PowerShell service stop/start + file deletion) | **BROKEN / FAKE** (`_shell` is `FakeShellService`, default simulation mode) | `ConfigViewModel.cs:41`, `WindowsConfigManager.cs:264-277` |
| Quick Fix: Reset Network / Winsock | `RunFixCommand("network_reset")` | `WindowsConfigManager.ResetNetworkAsync` | `netsh winsock reset; netsh int ip reset; ipconfig /flushdns` via `_shell` | **BROKEN / FAKE** (`_shell` is `FakeShellService`) | `ConfigViewModel.cs:41`, `WindowsConfigManager.cs:279-292` |
| Quick Fix: DISM & SFC Scan | `RunFixCommand("dism_sfc")` | `WindowsConfigManager.RunSystemCorruptionScanAsync` | `sfc /scannow; DISM /Online...` via `_shell` | **BROKEN / FAKE** (`_shell` is `FakeShellService`) | `ConfigViewModel.cs:41`, `WindowsConfigManager.cs:248-262` |
| Quick Fix: Reinstall WinGet | `RunFixCommand("winget_reinstall")` | `WindowsConfigManager.ReinstallWinGetAsync` | `Add-AppxPackage...` via `_shell` | **BROKEN / FAKE** (`_shell` is `FakeShellService`) | `ConfigViewModel.cs:41`, `WindowsConfigManager.cs:294-307` |
| Quick Fix: Sync Time (NTP) | `RunFixCommand("sync_time")` | `WindowsConfigManager.SetNtpServerAsync` | `w32tm /config /syncfromflags...` via `_shell` | **BROKEN / FAKE** (`_shell` is `FakeShellService`) | `ConfigViewModel.cs:41`, `WindowsConfigManager.cs:309-322` |
| OpenSSH Confirm Button | `ConfirmOpenSshCommand` | `WindowsConfigManager.EnableOpenSshServerAsync` | `Add-WindowsCapability...; Start-Service sshd` via `_shell` | **BROKEN / FAKE** (`_shell` is `FakeShellService`) | `ConfigViewModel.cs:41`, `WindowsConfigManager.cs:324-337` |
| 14 Legacy Control Panel Buttons | `LaunchPanelCommand(panel)` | `WindowsConfigManager.LaunchPanel` | `_shell.LaunchApplet(panel.Command)` | **BROKEN** (`_shell` is `FakeShellService`) | `ConfigViewModel.cs:41`, `WindowsConfigManager.cs:236-240` |
| **"Enable Features" Button** | `InstallSelectedFeaturesCommand` | `WindowsConfigManager.InstallFeaturesAsync` | `Enable-WindowsOptionalFeature...` via `_shell` | **BROKEN / FAKE** (`_shell` is `FakeShellService`) | `ConfigViewModel.cs:41`, `WindowsConfigManager.cs:139-169` |
| **"Disable Features" Button** | `UninstallSelectedFeaturesCommand` | `WindowsConfigManager.UninstallFeaturesAsync` | `Disable-WindowsOptionalFeature...` via `_shell` | **BROKEN / FAKE** (`_shell` is `FakeShellService`) | `ConfigViewModel.cs:41`, `WindowsConfigManager.cs:171-198` |

---

### 5. Tab 4 — Updates (`UpdatesTabView`)

| UI Element | Command / Handler | Service Method | Real Windows API / Command | Status | Evidence (File:Line) |
|---|---|---|---|:---:|---|
| Card 1: Default Profile Button | `SelectModeCommand(Default)` | `SelectedMode = Default` | None (UI state) | **REAL** | `UpdatesViewModel.cs:64` |
| Card 2: Security Profile Button | `SelectModeCommand(Security)` | `SelectedMode = Security` | None (UI state) | **REAL** | `UpdatesViewModel.cs:64` |
| Card 3: Disable All Profile Button | `SelectModeCommand(DisableAll)` | `SelectedMode = DisableAll`, requires typed confirmation | None (UI state) | **REAL** | `UpdatesViewModel.cs:64` |
| Typed Confirmation Box | Binding `TypedConfirmation` | Validates `"I understand"` to enable apply | None (UI state) | **REAL** | `UpdatesViewModel.cs:81` |
| **"Apply Selected Profile" Button** | `ApplySelectedModeCommand` | `WindowsUpdatesManager.ApplyModeAsync` | Modifies registry policies and Windows Update services (`wuauserv`, `BITS`, `UsoSvc`) | **BROKEN / FAKE** (`WindowsUpdatesManager` was injected with `fakeReg` and `fakeSvc` in `UpdatesViewModel`) | `UpdatesViewModel.cs:44-48, 93` |
| **Undo Update Profile** | None | None | None | **MISSING** (No undo action or button in UI) | `UpdatesTabView.xaml` |

---

### 6. Tab 5 — Toolbox (`ToolboxPage`)

| UI Element | Command / Handler | Service Method | Real Windows API / Command | Status | Evidence (File:Line) |
|---|---|---|---|:---:|---|
| Tool Switcher: Smart Cleaner | `SelectToolCommand("Cleaner")` | `ActiveTool = "Cleaner"` | None (UI nav) | **REAL** | `ToolboxPage.xaml:20`, `ViewModels.cs:86` |
| Tool Switcher: Startup Apps | `SelectToolCommand("Startup")` | `ActiveTool = "Startup"` | None (UI nav) | **REAL** | `ToolboxPage.xaml:22`, `ViewModels.cs:86` |
| Tool Switcher: Services Manager | `SelectToolCommand("Services")` | `ActiveTool = "Services"` | None (UI nav) | **REAL** | `ToolboxPage.xaml:24`, `ViewModels.cs:86` |
| Tool Switcher: Fixes Center | `SelectToolCommand("Fixes")` | `ActiveTool = "Fixes"` | None (UI nav) | **REAL** | `ToolboxPage.xaml:26`, `ViewModels.cs:86` |
| Tool Switcher: DNS Switcher | `SelectToolCommand("Network")` | `ActiveTool = "Network"` | None (UI nav) | **REAL** | `ToolboxPage.xaml:28`, `ViewModels.cs:86` |
| Cleaner Category Checkboxes (4) | Binding `category.IsSelected` | Updates selection boolean | None (UI state) | **REAL** | `ToolboxPage.xaml:77` |
| Cleaner "Scan Now" Button | `ScanCleanerCommand` | `SmartCleaner.ScanCategoriesAsync` | `DirectoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)` | **REAL** | `ToolboxPage.xaml:58`, `SmartCleaner.cs:36-75` |
| **Cleaner "Clean Selected" Button** | `CleanNowCommand` | `SmartCleaner.CleanCategoriesAsync` | `fi.Delete(); sub.Delete(true)` | **BROKEN / FAKE** (`_cleaner` hardcoded to `isSimulationMode: true`; only checks top-level files; no wuauserv stopping) | `ViewModels.cs:45, 116`, `SmartCleaner.cs:80, 103` |
| **Startup Apps List** | Populated in constructor | `StartupManager.GetStartupEntries` | None (`StartupManager.ReadRunKey` only reads if `_registry is FakeRegistryService`) | **BROKEN / FAKE** | `ViewModels.cs:77-80`, `StartupManager.cs:78` |
| **Startup Item ToggleSwitch** | `ToggleStartupItemCommand` | `StartupManager.SetEntryState` | None (writes to `fakeReg`) | **BROKEN / FAKE** | `ViewModels.cs:77, 128`, `StartupManager.cs:63` |
| **Services Manager List** | Populated in constructor | `ServicesManager.GetServices` | None (reads from `fakeSvc`) | **BROKEN / FAKE** | `ViewModels.cs:78, 81`, `ServicesManager.cs:28` |
| **Fixes Center "Run Repair" Button** | `RunFixCommand(fix)` | `FixesCenter.RunFixAsync` | In real mode: PowerShell process. But `_fixesCenter` is hardcoded to `isSimulationMode: true`! | **BROKEN / FAKE** (`Task.Delay(300)` only) | `ViewModels.cs:48, 134`, `FixesCenter.cs:94-98` |
| DNS Switcher "Ping Test" Button | `TestDnsLatencyCommand` | `DnsSwitcher.PingDnsAsync` | `System.Net.NetworkInformation.Ping.SendPingAsync` | **REAL** | `ToolboxPage.xaml:303`, `DnsSwitcher.cs:81-99` |
| **DNS Switcher "Apply DNS" Button** | `ApplyDnsCommand` | `DnsSwitcher.ApplyDnsAsync` | None! In real mode it is `await Task.CompletedTask; return true;` | **STUB / FAKE** | `ViewModels.cs:49`, `DnsSwitcher.cs:103-113` |

---

### 7. Overlays, Modals, and Legacy Pages

| UI Element | Command / Handler | Service Method | Real Windows API / Command | Status | Evidence (File:Line) |
|---|---|---|---|:---:|---|
| Command Palette Search Box | Binding `PaletteQuery` | In-memory search filter | None (UI state) | **REAL** | `MainWindow.xaml:289` |
| Command Palette Item Click | `ExecutePaletteItemCommand` | Switches tab or triggers action | None (UI nav) | **REAL** | `MainWindow.xaml:303`, `ViewModels.cs:727` |
| Config JSON Dialog Apply Button | `ImportConfigCommand` | Parses JSON, applies selection to tabs | `JsonSerializer.Deserialize` | **REAL** | `MainWindow.xaml:364`, `ViewModels.cs:665` |
| About Dialog Close Buttons | `CloseAboutDialogCommand` | Sets `IsAboutDialogOpen = false` | None (UI state) | **REAL** | `MainWindow.xaml:387, 399` |
| **DashboardPage.xaml** | `DashboardViewModel` | `SystemInfoProvider.GetSystemSummary` | Hardcoded `if (ramGb <= 0) ramGb = 16;` fallback. Dead page not accessible via tabs. | **DEAD CODE / FAKE** | `Views/DashboardPage.xaml`, `SystemInfoProvider.cs:31` |
| **AppsPage.xaml** | `AppsViewModel` | `WinGetManager` with `Task.Delay` simulation | Replaced by `InstallTabView`. | **DEAD CODE / FAKE** | `Views/AppsPage.xaml`, `WinGetManager.cs:93` |
| **PresetsPage.xaml** | `PresetsViewModel` | Duplicate presets page from v0.1.0 | Replaced by `TweaksTabView`. | **DEAD CODE** | `Views/PresetsPage.xaml` |
| **SettingsPage.xaml** | `SettingsViewModel` | Duplicate settings page from v0.1.0 | Handled in TitleBar and Config. | **DEAD CODE** | `Views/SettingsPage.xaml` |
| **UninstallerPage.xaml** | `AppUninstaller` | Returns 5 hardcoded fake apps; `Task.Delay(200)` uninstaller stub | Dead page from v0.1.0. | **DEAD CODE / FAKE** | `AppUninstaller.cs:34-80` |
| **RollbackHistoryPage.xaml** | `RollbackHistoryViewModel` | Reads journal files | Unused legacy sub-page. | **DEAD CODE** | `Views/RollbackHistoryPage.xaml` |

---

## 🔎 Repository-Wide Pattern Search

### 1. `NotImplementedException`
- **Count**: 0 instances.

### 2. `TODO` Comments
- **Count**: 0 genuine TODOs (1 false positive in `StateDetector.cs:159` matching `TryGetDouble`).

### 3. `Task.Delay` (Fake Progress & Simulated Execution)
**Total occurrences: 20 instances across 8 files**:
1. `InstallViewModel.cs:210`: Inside `UpgradeAllAsync` (`await Task.Delay(TestModeManager.IsTestMode ? 100 : 500)`).
2. `TweaksViewModel.cs:495`: Inside `RunTweaksAsync` (`await Task.Delay(30)`).
3. `TweaksViewModel.cs:534`: Inside `UndoSelectedTweaksAsync` (`await Task.Delay(30)`).
4. `ApplicationCatalogManager.cs:124`: Inside `InstallAppsAsync` (`await Task.Delay(50)`).
5. `ApplicationCatalogManager.cs:152`: Inside `UninstallAppsAsync` (`await Task.Delay(50)`).
6. `WinGetManager.cs:93`: Inside `InstallPackageAsync` (`await Task.Delay(100)`).
7. `WindowsConfigManager.cs:152`: Inside `InstallFeaturesAsync` (`await Task.Delay(50)`).
8. `WindowsConfigManager.cs:184`: Inside `UninstallFeaturesAsync` (`await Task.Delay(50)`).
9. `WindowsConfigManager.cs:253`: Inside `RunSystemCorruptionScanAsync` (`await Task.Delay(100)`).
10. `WindowsConfigManager.cs:269`: Inside `ResetWindowsUpdateAsync` (`await Task.Delay(100)`).
11. `WindowsConfigManager.cs:284`: Inside `ResetNetworkAsync` (`await Task.Delay(100)`).
12. `WindowsConfigManager.cs:299`: Inside `ReinstallWinGetAsync` (`await Task.Delay(100)`).
13. `WindowsConfigManager.cs:314`: Inside `SetNtpServerAsync` (`await Task.Delay(50)`).
14. `WindowsConfigManager.cs:329`: Inside `EnableOpenSshServerAsync` (`await Task.Delay(50)`).
15. `FixesCenter.cs:97`: Inside `RunFixAsync` (`await Task.Delay(300)`).
16. `DnsSwitcher.cs:106`: Inside `ApplyDnsAsync` (`await Task.Delay(200)`).
17. `AppUninstaller.cs:91`: Inside `UninstallAppAsync` (`await Task.Delay(200)`).
18. `WindowsUpdatesManager.cs:90`: Inside `ApplyDefaultModeAsync` (`await Task.Delay(50)`).
19. `WindowsUpdatesManager.cs:114`: Inside `ApplySecurityModeAsync` (`await Task.Delay(50)`).
20. `WindowsUpdatesManager.cs:142`: Inside `ApplyDisableAllModeAsync` (`await Task.Delay(50)`).

### 4. Hardcoded Sample Data
1. `AppUninstaller.cs:34-80`: 5 hardcoded fake apps (Mozilla Firefox, Visual Studio Code, 7-Zip, Git, Microsoft Teams).
2. `SystemInfoProvider.cs:31`: Hardcoded `16` GB RAM fallback when `TotalAvailableMemoryBytes` is non-positive.
3. `TweaksViewModel.cs:165-171`: Hardcoded `FakeRegistryService`, `FakeServicesService`, `FakeTaskSchedulerService`, `FakeRestoreService`, `FakeShellService`.
4. `ToolboxViewModel.cs:77-81`: Hardcoded `FakeRegistryService`, `FakeServicesService`.
5. `ConfigViewModel.cs:41`: Hardcoded `FakeShellService`.
6. `UpdatesViewModel.cs:44-46`: Hardcoded `FakeRegistryService`, `FakeServicesService`, `FakeTaskSchedulerService`.
7. `Program.cs (CLI):64-69`: Hardcoded in-memory fakes inside the standalone CLI executable.

### 5. Fake / Empty Command Handlers
1. `TweaksViewModel.RunTweaksAsync`: Does NOT invoke `_runner.ApplyTweakAsync`. Loops over items and sets boolean in memory.
2. `TweaksViewModel.UndoSelectedTweaksAsync`: Does NOT invoke `_runner.UndoTweakAsync`. Sets boolean to false in memory.
3. `InstallViewModel.UpgradeAllAsync`: Outputs 2 fake log strings and does `Task.Delay`.
4. `DnsSwitcher.ApplyDnsAsync`: Returns `Task.CompletedTask; return true;` without changing network adapter DNS.
5. `TweaksTabView DNS & Power Plan Dropdowns`: No event or command bound to selection changes.

---

# WinVelo Progress Tracker

Last Updated: 2026-10-14  
Repository Location: `C:\Dev\Tweakora`  
Current Release: `v1.0.0 (WinVelo Final Production Release)`  
Upstream Reference: [ChrisTitusTech/winutil](https://github.com/ChrisTitusTech/winutil) @ `07ccd8e2e755a706f31569808b31f5b77acad6a9`  

## Overall Status

| Phase | Description | Status | Build / Tests Result |
|---|---|---|---|
| **Phase 0** | Architecture, JSON Schema & Project Tree | **DONE** | Validated |
| **Phase 1** | Scaffolding, .NET 10, WPF UI, Mica, Crash Handlers | **DONE** | Build: 0 errors / Tests passed |
| **Phase 2** | Engine + Safety Layer (Rollback Journal, DryRun, Fakes, CLI) | **DONE** | Build: 0 errors / CLI works |
| **Phase 3** | Tweaks UI, Toggles, Badges, Presets | **DONE** | UI Cards & Filters / Tests passed |
| **Phase 4** | Dashboard, Startup Manager, Services Manager, Smart Cleaner | **DONE** | Build: 0 errors |
| **Phase 5** | App Installer (WinGet), Uninstaller, Fixes Center, Network Tools | **DONE** | Build: 0 errors |
| **Phase 6** | Expand Catalog to 80+ Verified Tweaks | **DONE** | Validated |
| **Phase 7** | Change History, Profiles, Command Palette (Ctrl+K), Settings | **DONE** | Build: 0 errors |
| **Phase 8** | Release Polish, Size Optimization, Packaging | **DONE** | Build: 0 errors |
| **Audit Phase** | Quality & Safety Audit, Staging Architecture, FlaUI Test Suite | **DONE** | Unit Tests passed / FlaUI UI Suite passed |
| **Phase A (Truth Inventory)** | Line-by-line audit of all buttons, commands, stubs, and Task.Delay | **DONE** | Created `FEATURE_MATRIX.md`. Audited every UI action. |
| **Phase B (Re-Architecture)** | DI injection, MVVM AsyncRelayCommand, Admin check | **DONE** | Fakes segregated to `Tweakora.Simulation`. 9 clean domain services in `Tweakora.Modules`. 4 architecture integrity gates pass. |
| **Phase D (Fix Every Feature)** | Make every row in FEATURE_MATRIX.md real and verified | **DONE** | All 83 active actions wired to real platform APIs. 0 fakes in production code. |
| **Phase E (UI Quality)** | Visual polish, button progress, screenshots | **DONE** | FlaUI UI automation passes across all 5 tabs and dialogs. |
| **Upstream Parity & Quality Audit** | Chris Titus WinUtil 1:1 Parity & Engine Verification | **DONE** | 141 verified tweaks synchronized. 64 unit/architecture tests pass. Non-destructive host detection (`--detect-all`) 100% clean. |
| **WinVelo Overhaul & Rebrand** | Full rebranding, Beginner Dashboard, Curated Winget Installer | **DONE** | Home Tab #1, 4 smart profiles, 8 quick-toggles, 36 branded apps, 66 unit tests + FlaUI passed. |
| **Real-PC Registry Sync** | Purge fake state, Win32 WM_SETTINGCHANGE broadcasts, Focus Auto-Sync | **DONE** | 74 unit tests passed / FlaUI passed / RealRegistrySyncTests passed |
| **Smart Optimization Upgrade** | Hardware Analyzer, Standby RAM Cleaner, DNS Latency Benchmark, Nagle's Disabler, 8 Gaming Tweaks | **DONE** | 80 unit tests passed / 149 tweaks verified / Release packages updated |
| **UX & Performance Overhaul** | Eliminate UI Freezes, Floating Activity HUD Pill, Card Spinners, Task.Yield Offloading | **DONE** | 80 unit tests passed / 1 FlaUI UI test passed / 0 UI Freezes |
| **Startup Apps Manager** | Native Windows Task Manager StartupApproved Bi-Directional Sync & Boot Speed Optimizer | **DONE** | 98 unit tests passed / 1 FlaUI UI test passed / 10 Screenshots Inspected |
| **Final Deep QA Audit** | Toggle loops, Single-file resilience, Startup parsing, 63/63 Self-Test Engine | **DONE** | 113/113 unit tests passed / 63/63 self-test passed / FlaUI passed |
| **Release v1.0.0** | Final Production Packaging, SHA256, Reproducible Build Script | **DONE** | WinVelo v1.0.0 Portable & Small zip packages created in `dist/`. |

---

## Native Windows Startup Apps Manager & Boot Speed Optimizer

### 1. Authentic Windows `StartupApproved` Binary Architecture
- **Bi-Directional Task Manager Sync**:
  - Implemented in `StartupService.cs` and `StartupHelpers.cs` without destructive registry deletions or file renaming (`.disabled`).
  - Uses Windows' official 12-byte `REG_BINARY` structure in `Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\`:
    - **Enabled**: `0x02` (or missing value).
    - **Disabled**: `0x03` + 64-bit UTC FILETIME timestamp (`DateTime.UtcNow.ToFileTimeUtc()`) encoded in bytes 4..11.
  - Automatically syncs bi-directionally with Windows Task Manager (`Taskmgr.exe`) and Windows Settings (`Settings -> Apps -> Startup`).
- **5 Comprehensive Windows Startup Discovery Locations**:
  1. `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (`User Registry`)
  2. `HKLM\Software\Microsoft\Windows\CurrentVersion\Run` (`System Registry`)
  3. `HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run` (`System Registry (x86)`)
  4. User Startup Folder: `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`
  5. Common Startup Folder: `%PROGRAMDATA%\Microsoft\Windows\Start Menu\Programs\Startup`

### 2. Intelligent Impact Evaluation & System Safety Protection
- **Hardware-Aware Boot Impact Classification**:
  - Inspects file size on disk and argument weight:
    - **High Impact**: File size > 15MB or heavy electron/gaming clients (Discord, Steam, Spotify, Teams, OneDrive, Adobe).
    - **Medium Impact**: File size 2MB - 15MB or multi-parameter commands.
    - **Low Impact**: File size < 2MB or lightweight helpers.
- **Core System Guard**:
  - Automatically safeguards essential hardware audio, GPU, and security services (`SecurityHealth`, `RtkAudUService`, `nvcontainer`, `AMDRSServ`) against 1-click batch disabling.
- **1-Click "Optimize Startup" Engine**:
  - Disables all non-essential High-Impact startup bloatware with a single click, saving up to 8–15 seconds of boot time while keeping critical system drivers active.

### 3. Modern Fluent Dark UI & Zero-Freeze Operations
- **Dedicated Sidebar Navigation**:
  - Rocket icon (`ui:SymbolRegular.Rocket24`) with live enabled-count badge pill.
- **Header KPI Metrics Cards**:
  - Real-time display of **Total Startup Apps**, **Enabled Apps**, **High Impact Apps**, and **Estimated Boot Delay / Time Saved**.
- **Interactive Control Deck**:
  - Live search filter across application names, publishers, commands, and file paths.
  - Quick filter pills (`All`, `Enabled`, `Disabled`, `High Impact`).
  - Open File Location button launching `explorer.exe /select,"<exe>"`.
- **Zero UI Freeze & Focus Auto-Sync**:
  - Universal async offloading (`await Task.Yield()`, `Task.Run`) with `SemaphoreSlim` concurrency locks.
  - Seamlessly re-syncs state when switching back from Windows Settings or Task Manager (`RefreshActiveStatesOnFocusAsync`).

---

## WinVelo Zero-Freeze Architecture & Live Animated HUD

### 1. Root Cause Elimination of UI Freezes
- **Non-Blocking Shell & Setting Notifications (`WindowsSettingNotifier.cs`)**:
  - Removed synchronous `WindowsSettingNotifier` calls from inside individual `WindowsRegistryService` writes.
  - Dispatches `NotifySettingsChangedAsync` only once at the completion of a batch or tweak operation inside a background `Task.Run`.
  - Configured with `SMTO_ABORTIFHUNG | SMTO_NOTIMEOUTIFNOTHUNG`, strict `50ms` timeout, and `SHCNF_IDLIST` (never `SHCNF_FLUSH`), eliminating multi-second Windows Explorer hangs.
- **Universal Off-Thread Execution (`Task.Yield` + `Task.Run`)**:
  - Re-architected all ViewModels (`DashboardViewModel`, `StartupViewModel`, `TweaksViewModel`, `ToolboxViewModel`, `InstallViewModel`, `ConfigViewModel`, `UpdatesViewModel`, `BackupsViewModel`, `MainViewModel`).
  - Every asynchronous operation immediately yields the UI Dispatcher via `await Task.Yield()`, enabling the UI to paint spinners and update button states instantly before offloading heavy work to `Task.Run`.

### 2. Modern Animated Loading Indicators & Floating Activity HUD
- **Sleek Floating Activity HUD Pill (`MainWindow.xaml` & `MainViewModel.cs`)**:
  - Positioned top-center with `#1E2433` dark pill background, glowing `#3B82F6` border, drop shadow, and `ZIndex="1000"`.
  - Driven by thread-safe `IActivityTracker` in `Tweakora.Core.Operations`: displays animated `ui:ProgressRing` with live action status (e.g. `Applying tweak (3/14)...`).
  - Upon completion, transitions to emerald green checkmark `"✓ Completed in 0.4s"` and auto-dismisses after 1.2s using non-blocking `DispatcherTimer` (0 `Task.Delay` in production code).
- **Per-Card & Per-Button Animated Busy States**:
  - Tweak, quick toggle, and startup cards display a 16x16 `ui:ProgressRing` during active toggling.
  - Direct 1-click toggle switches and preset buttons are cleanly disabled (`IsEnabled="{Binding IsNotToggling}"`) during operations to prevent duplicate race conditions.

### 3. Quality, Verification & Packaging
- **113/113 Unit Tests PASSED** (0 failures).
- **FlaUI UI Automation Test PASSED** across all tabs and overlays.
- **63/63 CLI Self-Test Checks PASSED** (100% roundtrip pass rate).
- **All 10 Screenshots Refreshed & Visually Inspected** in `docs/screenshots/` (dashboard, startup, toolbox, tweaks, backups, config, command-palette, diff-preview, install, updates).
- **Release Packages (`dist/`)**:
  - `dist/WinVelo-v1.0.0-win-x64-portable.zip`
  - `dist/WinVelo-v1.0.0-win-x64-small.zip`
  - Checksums recorded in `dist/SHA256SUMS.txt`.

---

## Instructions for Host Testing

You can safely test Level 1 (HKCU-only) changes anytime by running:
```powershell
.\dist\WinVelo-v1.0.0-win-x64-portable\winvelo-cli.exe --self-test --level 1
```
It creates an automatic restore point, tests each HKCU tweak, immediately reverts it, and verifies that the exact original registry state is restored.

To launch the modern GUI:
```powershell
.\dist\WinVelo-v1.0.0-win-x64-portable\WinVelo.exe
```



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
| **Release v1.0.0** | Final Production Packaging, SHA256, Screenshots | **DONE** | WinVelo v1.0.0 Portable & Small zip packages created in `dist/`. |

---

## WinVelo v1.0.0 Real Windows Settings Bi-Directional Sync Overhaul

### 1. Root Cause Resolution & Engine Hardening
- **Complete Elimination of Synthetic State Markers**: Purged all occurrences of `SOFTWARE\WinVelo\State` across `Catalog/Tweaks/*.json`. All tweaks now inspect and manipulate authentic Windows Registry keys (`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced`, `HKCU\Software\Microsoft\Windows\CurrentVersion\Search`, `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32`, etc.).
- **Non-Blocking System Restore Fallback**: In `TweakRunner.cs`, if System Restore creation is throttled or unavailable on Windows Home editions (and no interactive prompt handler was supplied), the runner logs a diagnostic warning and proceeds safely with the Write-Ahead Rollback Journal and full `.reg` registry snapshot backup instead of aborting tweak execution.
- **Win32 Shell & Settings Notifications**: Implemented `WindowsSettingNotifier` in `Tweakora.Platform`, dispatching `SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, ...)` and `SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, ...)` upon registry writes, ensuring Windows Explorer, the Taskbar, and Windows Settings reflect changes immediately.
- **Bi-Directional Focus Synchronization**: Bound `MainWindow.Activated` to trigger background state re-detection via `RefreshActiveStatesOnFocusAsync()`. When the user switches to Windows Settings, toggles an option, and clicks back into WinVelo, the UI toggles update automatically to reflect the actual OS state.
- **8 Core Dashboard Quick Toggles**: Corrected registry mappings, inverted value logic, origin keys, and WPF command binding parameters (`CommandParameter="{Binding IsChecked, RelativeSource={RelativeSource Self}}"`) across Dark Mode, File Extensions, Bing Search, Classic Context Menu, Background Apps, Spotlight Ads, Taskbar End Task, and Telemetry.
- **Dedicated Automated Verification**: Added `RealRegistrySyncTests.cs` testing bi-directional state detection and execution across all 8 core toggles.

### 2. Quality & Verification
- **74/74 Unit, Golden, Architecture & Sync Tests** in `Tweakora.Tests` PASSED.
- **FlaUI UIA3 Automation Test Suite** in `Tweakora.UiTests` PASSED.
- **9 High-Resolution 1440x900 Screenshots** refreshed in `docs/screenshots/`.
- **Release Packages (`dist/`)**:
  - `dist/WinVelo-v1.0.0-win-x64-portable.zip` (SHA-256: `68B52C576D407FBCD24B34905A131A5B5A3CC9E411742AEB5841A41F98D4CDCE`)
  - `dist/WinVelo-v1.0.0-win-x64-small.zip` (SHA-256: `C5213B2C477ACBFE1B29E19626FCB5539BA2F78933D38182A219F1F56AD02263`)
  - `dist/SHA256SUMS.txt` updated.

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



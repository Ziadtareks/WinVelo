# WinVelo Architecture Specification

WinVelo is an open-source, modular, data-driven Windows 10/11 system configuration and toolbox platform. It is engineered with a strict separation between UI, business logic, platform operations, and execution environments.

---

## 1. High-Level System Layers

```mermaid
graph TD
    App[WinVelo.App - WPF & Fluent UI] --> Modules[WinVelo.Modules - Domain Services]
    App --> Core[WinVelo.Core - Catalog & Journal]
    App --> Simulation[WinVelo.Simulation - In-Memory Fakes]
    Modules --> Core
    Modules --> Platform[WinVelo.Platform - Win32 Drivers]
    Core --> Platform
    CLI[WinVelo.Cli - Headless Runner] --> Core
    CLI --> Platform
    CLI --> Simulation
    Platform --> OS[Windows 10 / 11 Kernel & Win32]
```

---

## 2. Project Responsibilities

### `WinVelo.Core` (No UI Dependencies)
- **Target Framework**: `.NET 10`
- **Models**:
  - `TweakDefinition`: Data-contract representation of a tweak complying with `tweak.schema.json`.
  - `ActionItem`: Discrete operation definitions (`Registry`, `Service`, `Task`, `File`, `OptionalFeature`).
  - `DetectRule`: State detection rules supporting `all`, `any`, and single checks.
  - `WinVeloProfile`: Multi-tweak bundles and serialization engines (JSON, `.reg`, PowerShell `.ps1`).
- **Engine Components**:
  - `RollbackJournalManager`: Write-ahead logging manager saving snapshots in `%APPDATA%\WinVelo\Journal`. Always preserves initial baseline state.
  - `OperationRunner`: Universal task executor coordinating operations, journal records, and live UI activity tracking.
  - `StateDetector`: Evaluates whether a tweak is currently applied on the system.
  - `RegistryActionHandler`, `ServicesActionHandler`, `TasksActionHandler`: Modular handlers executing discrete system actions.

### `WinVelo.Simulation` (In-Memory Mock Layer)
- **Target Framework**: `.NET 10`
- Isolated test drivers implementing platform interfaces for headless unit tests and FlaUI test runs:
  - `FakeRegistryService`: In-memory simulated registry supporting HKCU, HKLM, and HKCR hives.
  - `FakeServicesService`: In-memory service state tracker with configurable initial states.
  - `FakeTaskSchedulerService`: In-memory scheduled task status manager.
  - `FakeRestoreService`: Simulated restore point creator with validation tracking.
  - `FakeShellService`: Safe process and command simulator preventing actual script execution.

### `WinVelo.Platform` (Windows Native APIs)
- **Target Framework**: `.NET 10 (Windows)`
- Implements real Win32 interfaces:
  - `WindowsRegistryService`: Direct Windows Registry manipulation across `HKCU`, `HKLM`, and `HKCR`.
  - `WindowsServicesService`: Native `Advapi32.dll` calls (`OpenSCManager`, `OpenService`, `ChangeServiceConfig`).
  - `WindowsTaskSchedulerService`: Native Windows Task Scheduler integration.
  - `WindowsRestoreService`: Win32 `sfc.dll` / `srclient.dll` calling `SRSetRestorePointW`.
  - `WindowsShellService`: Native process launcher and elevated execution engine.
  - `WindowsSettingNotifier`: Win32 `SendMessageTimeout` (`WM_SETTINGCHANGE`) and `SHChangeNotify` change broadcasting.

### `WinVelo.Modules` (Domain Services)
- `WinVelo.Modules.Advisor`: Hardware profile inspection and optimization recommendations (`SmartAdvisorService`).
- `WinVelo.Modules.Apps`: Curated software catalog and package manager integration (`AppInstallService`).
- `WinVelo.Modules.Cleaner`: Whitelisted temporary file previewer and cleaner (`CleanerService`).
- `WinVelo.Modules.Config`: Feature toggling via DISM (`FeatureService`) and system repair recipes (`FixService`).
- `WinVelo.Modules.Memory`: Standby list memory cleaner and cache purge (`MemoryOptimizerService`).
- `WinVelo.Modules.Network`: Fast DNS switcher with latency testing (`DnsService`) and TCP/Nagle optimizer (`NetworkOptimizerService`).
- `WinVelo.Modules.Services`: Windows background service management (`ServicesManagerService`).
- `WinVelo.Modules.Startup`: Task Manager `StartupApproved` sync and boot speed optimizer (`StartupService`).
- `WinVelo.Modules.Tweaks`: Catalog loader, detector, and execution orchestrator (`TweakService`).
- `WinVelo.Modules.Updates`: Windows Update mode switcher (`UpdateModeService`).

### `WinVelo.App` (Presentation Layer)
- **UI Framework**: WPF with `WPF-UI` (`lepoco/wpfui` 4.0.3).
- **Themes & Materials**: Mica backdrop on Windows 11 with seamless solid background fallback on Windows 10. Automatic Light and Dark theme adaptation.
- **Localization**: English-only user interface (`LocalizationManager`).
- **Keyboard Navigation**: Command Palette modal accessible via `Ctrl+K`.
- **Diagnostics**: Structured file logging using `Serilog` (`LoggingService`), rolling daily logs to `%LOCALAPPDATA%\WinVelo\Logs`.

### `WinVelo.Cli` (Headless Automation)
- Command-line executable (`winvelo-cli.exe`) for unattended configuration and deployment scripts:
  ```powershell
  # List all catalog tweaks
  winvelo-cli.exe --list

  # Preview standard preset in dry-run mode
  winvelo-cli.exe --preset Standard --dry-run

  # Apply a specific tweak against host OS
  winvelo-cli.exe --tweak "ui.explorer.classic-context-menu" --apply --real-mode

  # Revert a specific tweak
  winvelo-cli.exe --tweak "ui.explorer.classic-context-menu" --undo --real-mode

  # HKCU apply/undo round trip of every Level 1 tweak (simulation unless --real-mode is given)
  winvelo-cli.exe --self-test --level 1
  winvelo-cli.exe --self-test --level 1 --real-mode   # on the real PC, creates a restore point first
  ```

---

## 3. Directory Layout

```text
C:\Dev\WinVelo\
├── Catalog\                     # Strict JSON definitions
│   ├── Apps\                    # Curated applications catalog
│   ├── Config\                  # Features, appx, and DNS presets
│   ├── Presets\                 # Curated system profiles
│   ├── schema\                  # tweak.schema.json validation schema
│   ├── Tweaks\                  # 142 tweak definitions
│   ├── Localization\            # Arabic display text for tweaks and presets (ar.json)
│   └── overrides.json           # Safety overrides & baseline exclusions
├── dist\                        # Build output of build\publish.ps1 / release.ps1 (not committed)
├── docs\                        # Architecture, Safety, Roadmap, and Guides
├── src\
│   ├── WinVelo.App\            # WPF Views, ViewModels, Resources (WinVelo.exe)
│   ├── WinVelo.Cli\            # Headless command-line tool (winvelo-cli.exe)
│   ├── WinVelo.Core\           # Engine, models, journal, activity tracking
│   ├── WinVelo.Modules\        # Domain feature implementations (Tweaks, Apps, Startup, etc.)
│   ├── WinVelo.Platform\       # Windows native Win32 drivers
│   ├── WinVelo.Simulation\     # In-memory mock drivers for testing
│   ├── WinVelo.Tests\          # xUnit unit & integration test suite (135 tests)
│   └── WinVelo.UiTests\        # FlaUI UI automation test suite
└── tools\
    ├── CatalogValidator\        # Schema verification utility
    ├── SourceChecker\           # Live HTTP source documentation link checker
    └── UpstreamImporter\        # Stages upstream source proposals for review
```

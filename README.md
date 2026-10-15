# WinVelo ⚡🛡️

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)]()
[![.NET Version](https://img.shields.io/badge/.NET-10.0%20LTS-purple.svg)](https://dotnet.microsoft.com/)
[![Windows Target](https://img.shields.io/badge/Windows-10%20%7C%2011-blue.svg)]()
[![Verified Tweaks](https://img.shields.io/badge/catalog%20tweaks-149-success.svg)]()
[![Winget Apps](https://img.shields.io/badge/curated%20apps-36%20essential-blue.svg)]()
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Safety](https://img.shields.io/badge/safety-reversible%20by%20design-blueviolet.svg)](docs/SAFETY.md)

**WinVelo** is a transparent, calm, open-source Windows 10/11 configuration utility and package manager. Built with C# and .NET 10 LTS in the design philosophy of O&O ShutUp10++, it provides full visibility into system settings with exact registry keys, cited vendor documentation, direct Windows Settings equivalents, and write-ahead rollback safety.

---

## 📸 Visual Showcase

### Home (System Status)
Calm status overview showing administrator privilege, System Restore state with latest restore point date, exact count of tweaks applied by WinVelo, Windows servicing health result, and preset cards.
![WinVelo Home](docs/screenshots/dashboard.png)

### Tweaks (Catalog & Exact Changes)
149 verified tweaks categorized by risk (Safe, Low, Medium, High) with honest state badges (Applied, Partial, Not applied, Can't verify), auto-generated exact changes, and cited source documentation.
![WinVelo Tweaks](docs/screenshots/tweaks.png)

### Apps (Curated Winget Manager)
Curated package installer across Browsers, Social, Media, Utilities, Gaming, and Development with live installation progress.
![WinVelo Apps](docs/screenshots/install.png)

### Tools (Consolidated System Hub)
Consolidated toolkit housing Startup Apps, Windows Services, System Cleaner, DNS switcher, Windows Fixes, Windows Updates mode, Optional Features, and Restore Points.
![WinVelo Tools](docs/screenshots/tools.png)

### History & Rollback Journal
Complete rollback journal listing sessions newest first, grouped tweak statuses, single-click session and tweak undo, "Undo all WinVelo changes", `.txt` log exports, and read-only System Restore points.
![WinVelo History](docs/screenshots/history.png)

---

## 🌟 Key Features & 5-Tab Architecture

### 1. 🏠 Home
- **Calm System Status Tiles**:
  - Administrator Privilege indicator.
  - System Restore status tile with date of latest restore point.
  - Neutral count of active tweaks applied by WinVelo with no gamified scores or meters.
  - Windows Servicing Health state (Component-Based Servicing integrity check).
- **Pending Action Banners**: Prominent contextual alerts for pending reboots or explorer restarts.
- **Preset Cards**: Preset profiles (Recommended, Gaming, Privacy) opening the unified preview dialog.

### 2. ⚡ Tweaks
- **Experience Modes**:
  - **Simple Mode**: Displays only Recommended settings (Safe or Low risk, reversible, documented or UI equivalent).
  - **Advanced Mode**: Displays the entire catalog with Experimental settings behind a dedicated toggle.
- **Transparent Information Panel**:
  - Direct Windows location for `ui-equivalent` tweaks (e.g., *Settings › Personalization › Colors*).
  - Cited vendor documentation with exact quoted strings for `documented` tweaks.
  - Auto-generated "Exact changes" section detailing hives, keys, values, and service configurations.
- **Honest Execution Flow**:
  - Safe and Low tweaks apply immediately with an instant undo toast notification.
  - Medium and High tweaks require explicit preview and confirmation in a shared dialog.
  - Unified Preview and Result modal dialogs with Windows servicing health verification.

### 3. 📦 Apps
- 36 popular packages curated across Browsers, Social, Media, Utilities, Gaming, and Dev.
- Asynchronous Winget execution with installed status detection and live logging.

### 4. 🧰 Tools
A single consolidated hub for system utilities:
- **Startup Apps**: Inspect and manage programs configured to run on Windows logon.
- **Windows Services**: Inspect system service startup types and running states.
- **System Cleaner**: Safe temporary file and cache cleaning (%TEMP%, Windows Update cache).
- **DNS Switcher**: Configure encrypted DNS resolvers (Cloudflare, Quad9, Google, AdGuard).
- **Windows Fixes**: SFC and DISM servicing repairs, network resets, time synchronization, and Windows shortcuts.
- **Windows Updates Mode**: Choose between Default, Security Only (Recommended), or Disabled (requires typed confirmation).
- **Optional Features**: Manage optional platform components (WSL, Hyper-V, Windows Sandbox).
- **Restore Points**: View and create Windows System Restore points.

### 5. 🛡️ History & Rollback Journal
- **Session History (Newest First)**: Inspect past WinVelo modification sessions with timestamps, trigger source (Preset, Manual, CLI), and touch status.
- **Grouped Tweak Statuses**: View touched tweaks with active vs reverted states and exact action records.
- **Rollback Actions**:
  - Undo individual tweaks.
  - Undo entire sessions.
  - **Undo All WinVelo Changes**: Reverts all changes recorded in the WinVelo rollback journal.
- **Session Export**: Export complete session audit logs as plaintext files (`.txt`).
- **Read-Only Restore Points**: Inspect all system restore points on the machine.

---

## 💾 Downloads (Release v1.0.0)

| Package | Type | Size | SHA256 Checksum |
|---|---|---|---|
| **[WinVelo-v1.0.0-win-x64-portable.zip](dist/WinVelo-v1.0.0-win-x64-portable.zip)** | Standalone Portable (Includes .NET Runtime) | ~95 MB | See [dist/SHA256SUMS.txt](dist/SHA256SUMS.txt) |
| **[WinVelo-v1.0.0-win-x64-small.zip](dist/WinVelo-v1.0.0-win-x64-small.zip)** | Framework Dependent (Requires .NET 10) | ~3.6 MB | See [dist/SHA256SUMS.txt](dist/SHA256SUMS.txt) |

---

## ⌨️ Command-Line Interface (`winvelo-cli`)

WinVelo includes a dedicated automation CLI (`winvelo-cli.exe`):

```powershell
# Run simulated self-test with rollback verification
winvelo-cli.exe --test-mode --self-test --level 1

# List available catalog tweaks
winvelo-cli.exe --list

# Inspect exact changes and documentation for a tweak
winvelo-cli.exe --info "ui.explorer.classic-context-menu"

# Apply or preview an exported configuration JSON
winvelo-cli.exe --config my-setup.json --run
winvelo-cli.exe --config my-setup.json --dry-run

# Revert a specific tweak
winvelo-cli.exe --undo "ui.explorer.classic-context-menu"
```

---

## 🚀 Building from Source

### Prerequisites
- Windows 10 (Build 19041+) or Windows 11
- [.NET 10 SDK (LTS)](https://dotnet.microsoft.com/)

### Clone and Build
```powershell
git clone https://github.com/winvelo/winvelo.git
cd winvelo

# Build solution
dotnet build Tweakora.sln

# Run unit tests (200 tests)
dotnet test src/Tweakora.Tests/Tweakora.Tests.csproj

# Run UI tests in simulated test mode
dotnet test src/Tweakora.UiTests/Tweakora.UiTests.csproj

# Run application in safe simulated test mode
dotnet run --project src/Tweakora.App/Tweakora.App.csproj -- --test-mode
```

---

## 🏷️ Naming

WinVelo was previously developed under the project name **Tweakora**. To avoid breaking internal solution references, package configurations, and project dependencies, internal C# namespaces, project files (`.csproj`), directory names, and the solution file (`Tweakora.sln`) retain the `Tweakora.*` prefix. All user-facing executables (`WinVelo.exe`, `winvelo-cli.exe`), desktop shortcuts, installer packages, documentation, and user data directories use the **WinVelo** name.

---

## 📄 License

WinVelo is released under the **[MIT License](LICENSE)**.

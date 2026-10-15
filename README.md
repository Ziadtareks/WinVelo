# WinVelo 🛡️

[![Release](https://img.shields.io/badge/release-v0.9.0--beta-blue.svg)](https://github.com/winvelo/winvelo/releases)
[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)]()
[![.NET Version](https://img.shields.io/badge/.NET-10.0%20LTS-purple.svg)](https://dotnet.microsoft.com/)
[![Windows Target](https://img.shields.io/badge/Windows-10%20%7C%2011-blue.svg)]()
[![Catalog Tweaks](https://img.shields.io/badge/verified%20tweaks-149-success.svg)]()
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

**WinVelo** is an honest, calm, open-source Windows 10/11 configuration utility and package manager. Built in the design style of O&O ShutUp10++, it gives you full control and transparency over your system settings with verifiable evidence, exact registry keys, cited vendor documentation, and write-ahead rollback safety.

---

## 🎯 What WinVelo Does & Does NOT Do

### What WinVelo Does
- **Full Transparency**: Every tweak displays its exact registry keys, values, and service configurations before and after application.
- **Evidence-Based Documentation**: Every setting cites official vendor documentation (Microsoft Learn / policy CSPs) or provides its exact breadcrumb path in Windows Settings (e.g., *Settings › Personalization › Colors*).
- **Transactional Rollback Journal**: Logs original pre-modification system baselines to `%APPDATA%\WinVelo\Journal` to ensure true undo capability.
- **Safety Safeguards**: Prompts for System Restore point creation and verifies restore points before making system adjustments.
- **Curated Package Manager**: Installs verified software via standard Windows Package Manager (`winget`) without bundled adware.

### What WinVelo Does NOT Do
- ❌ **No "Speed Boosts" or Placebos**: We do not claim arbitrary "+15% FPS" gains, lower mouse input lag by 5ms, or run deceptive "RAM purges" that harm cache efficiency.
- ❌ **No Telemetry or Phone-Homes**: WinVelo contains zero telemetry, analytics, user tracking, or third-party advertising SDKs.
- ❌ **No Silent or Automatic Changes**: Nothing is applied without your explicit choice and confirmation.

---

## 🛡️ Safety Model

1. **Pre-Flight Restore Point**: Offers to create and verify a Windows System Restore point before applying changes.
2. **Preview Before Applying**: High-risk, medium-risk, and batch actions enforce the Unified Execution Dialog showing the exact registry differences before applying.
3. **Write-Ahead Baseline Journal**: Captures the state of your system before writing changes, enabling reliable single-tweak, session, or global rollbacks.
4. **Instant Toast Undo**: Safe and Low-risk tweaks applied with a single click provide an instant "Undo" toast notification.
5. **Crash Recovery Guard**: Interrupted or terminated sessions are detected on next launch with an automated rollback prompt.

---

## ⚠️ Limitations & Disclaimers

- **Administrator Privileges**: Applying system-wide tweaks (`HKLM`), managing services, or creating restore points requires Administrator elevation.
- **Irreversible Tweaks**: Certain actions (e.g., permanent Edge removal, OneDrive uninstallation, Disk Cleanup) cannot be undone via registry inversion and are strictly flagged as irreversible with extra confirmation guards.
- **Unsigned Binaries (SmartScreen)**: As a new open-source project, binaries are not yet code-signed with an expensive EV certificate. Windows SmartScreen may display an *"Unknown Publisher"* warning. You can verify package hashes against our signed `SHA256SUMS.txt` or build directly from source.

---

## 📸 Screenshots

### Home (Calm System Status)
Honest status overview showing administrator privilege, System Restore state with latest restore point date, exact count of tweaks applied by WinVelo, servicing health, and preset cards.
![WinVelo Home](docs/screenshots/dashboard.png)

### Tweaks (Catalog & Exact Changes)
149 verified tweaks categorized by risk (Safe, Low, Medium, High) with honest state badges (Applied, Partial, Not applied, Can't verify), auto-generated exact changes, and cited source documentation.
![WinVelo Tweaks](docs/screenshots/tweaks.png)

### Apps (Curated Package Manager)
Curated package installer across Browsers, Social, Media, Utilities, Gaming, and Development with live installation progress.
![WinVelo Apps](docs/screenshots/install.png)

### Tools (Consolidated System Hub)
Consolidated toolkit housing Startup Apps, Windows Services, System Cleaner, DNS switcher, Windows Fixes, Windows Updates mode, Optional Features, and Restore Points.
![WinVelo Tools](docs/screenshots/tools.png)

### History & Rollback Journal
Complete rollback journal listing sessions newest first, grouped tweak statuses, single-click session and tweak undo, "Undo all WinVelo changes", and diagnostic export.
![WinVelo History](docs/screenshots/history.png)

---

## 💾 Downloads (Release v0.9.0-beta)

| Package | Type | Size | Verification |
|---|---|---|---|
| **[WinVelo-v0.9.0-beta-win-x64-portable.zip](dist/WinVelo-v0.9.0-beta-win-x64-portable.zip)** | Standalone Portable (Includes .NET Runtime) | ~95 MB | See [dist/SHA256SUMS.txt](dist/SHA256SUMS.txt) |
| **[WinVelo-v0.9.0-beta-win-x64-small.zip](dist/WinVelo-v0.9.0-beta-win-x64-small.zip)** | Framework Dependent (Requires .NET 10) | ~3.6 MB | See [dist/SHA256SUMS.txt](dist/SHA256SUMS.txt) |

---

## ⌨️ Command-Line Interface (`winvelo-cli`)

WinVelo includes a dedicated automation CLI (`winvelo-cli.exe`):

```powershell
# Safe read-only inspection of a tweak with documentation and exact changes
winvelo-cli.exe --info "pref.darkmode"

# Non-destructive simulation preview of all tweaks
winvelo-cli.exe --dry-run

# Run safe automated self-test in simulated test mode
winvelo-cli.exe --test-mode --self-test --level 1

# Export read-only diagnostic bundle for bug reports (sanitized of user paths)
winvelo-cli.exe --diagnostics WinVelo-Diagnostics.zip

# Revert a specific tweak
winvelo-cli.exe --undo "pref.darkmode"
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

# Build solution (0 warnings, 0 errors)
dotnet build Tweakora.sln -c Release

# Run unit tests
dotnet test src/Tweakora.Tests/Tweakora.Tests.csproj

# Run UI tests in safe simulation test mode
dotnet test src/Tweakora.UiTests/Tweakora.UiTests.csproj

# Validate catalog schema integrity
dotnet run --project tools/CatalogValidator/CatalogValidator.csproj

# Launch WinVelo in safe simulated test mode
dotnet run --project src/Tweakora.App/Tweakora.App.csproj -- --test-mode
```

---

## 🙏 Credits & Acknowledgments

- **WinUtil (Chris Titus Tech)**: We express sincere appreciation to Chris Titus Tech and contributors to [winutil](https://github.com/ChrisTitusTech/winutil) (licensed under MIT). WinUtil inspired several foundational PowerShell and Windows servicing automation concepts ported to WinVelo's C#/.NET 10 engine.
- **O&O ShutUp10++**: The UI design, calm aesthetic, and non-intrusive presentation were directly inspired by the clarity of O&O ShutUp10++.
- **WPF UI**: Fluent design controls by [lepoco/wpfui](https://github.com/lepoco/wpfui).

---

## 📄 License & Governance

- License: [MIT License](LICENSE)
- Privacy Policy: [PRIVACY.md](PRIVACY.md)
- Security Policy: [SECURITY.md](SECURITY.md)
- Contributing Guide: [CONTRIBUTING.md](CONTRIBUTING.md)

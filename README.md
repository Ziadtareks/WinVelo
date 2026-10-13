# Tweakora 🛡️

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)]()
[![.NET Version](https://img.shields.io/badge/.NET-10.0%20LTS-purple.svg)](https://dotnet.microsoft.com/)
[![Windows Target](https://img.shields.io/badge/Windows-10%20%7C%2011-blue.svg)]()
[![Verified Tweaks](https://img.shields.io/badge/verified%20tweaks-141-success.svg)]()
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Safety](https://img.shields.io/badge/safety-reversible%20by%20design-blueviolet.svg)](docs/SAFETY.md)

**Tweakora** is a professional, open-source Windows 10/11 system configuration center and toolbox utility. Built with C# and .NET 10 LTS, it pairs the full feature set and layout of Chris Titus Tech's WinUtil with Tweakora's safety architecture (write-ahead rollback journal, dry-run diff preview, and 100% reversible tweaks).

> **Credits**: Based on ideas and tweak data from [ChrisTitusTech/winutil](https://github.com/ChrisTitusTech/winutil) (MIT License).

---

## 🌟 Top-Level Navigation & Layout

Tweakora features 5 top horizontal tabs with real-time live search filtering:

1. **`INSTALL`**: Dense multi-column catalog of 236 curated applications across 10 categories (Browsers, Dev, Documents, Games, Multimedia, Utilities, Tools, Social, Security, System) with WinGet & Chocolatey support.
2. **`TWEAKS`**: 3-column layout matching WinUtil:
   - **Column 1 — Essential Tweaks**: Restore Point, Disable Telemetry, Diagnostic Data, Activity History, Bing Search in Start, Location Tracking, etc.
   - **Column 2 — Advanced Tweaks (CAUTION)**: BitLocker disable with prominent red safety card, Edge/Brave debloat, OneDrive removal, Home & Gallery removal.
   - **Column 3 — Customize Preferences**: 25 direct toggles (Dark Mode, File Extensions, Verbose Logon, Mouse Acceleration, End Task on Right-Click, etc.), DNS provider switcher (Cloudflare, Quad9, Google, AdGuard, NextDNS), and Ultimate Performance power plan.
   - **Tweakora Extras**: Collapsible expander with all additional verified privacy, AI, gaming, and system tweaks.
   - **Presets**: `Standard`, `Minimal`, `Advanced`, and `Clear`.
3. **`CONFIG`**:
   - 33 Windows optional features with real-time status detection.
   - 14 Legacy Windows Control Panel applets (God Mode, Control Panel, Network Connections, Device Manager, Sound, etc.).
   - Quick Fixes: Autologin helper, Reset Windows Update cache, Network reset, DISM / SFC repair, WinGet reinstaller, and NTP time sync.
   - OpenSSH Server setup with explicit confirmation.
4. **`UPDATES`**:
   - **Default Settings (Out of Box)**: Restores Microsoft standard update behavior.
   - **Recommended (Security Focus)**: Delays feature upgrades by 2 years while installing security updates smoothly.
   - **Disable All Updates (NOT Recommended)**: Disables update services with typed "I understand" confirmation.
5. **`TOOLBOX`**:
   - Built-in maintenance tools: Startup Manager, Services Manager, Smart Cleaner, App Uninstaller, Fixes Center, and Rollback History.

---

## 🛡️ Reversible by Design

- **Write-Ahead Rollback Journal**: Saves your computer's exact original state to disk *before* making any change. If you ever want to revert, your original settings are restored with one click.
- **System Restore Shield**: Automatically checks and verifies Windows System Restore points before applying modifications.
- **Dry-Run Diff Preview**: Inspect the exact registry keys, values, and services that will be changed before touching anything.
- **Config Export / Import**: Save full system presets to JSON files, and automate them with `tweakora.exe --config <path> --run`.

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl + K` | Open Command Palette |
| `Escape` | Close Command Palette / Dialogs |

---

## 🛠️ Architecture

Tweakora uses a clean, decoupled 5-tier architecture:

```text
[Tweakora.App (WPF)] -> [Tweakora.Modules] -> [Tweakora.Platform] -> [Windows OS]
        \                      /
         v                    v
           [Tweakora.Core]
```

- **`Tweakora.Core`**: Pure .NET 10 logic. Catalog loaders, journal manager, state detector, and dry-run engine. 100% testable without Windows dependencies.
- **`Tweakora.Platform`**: Native Win32 API bindings (Registry, Service Control Manager, Task Scheduler, System Restore).
- **`Tweakora.Modules`**: Business implementations for Startup, Services, Cleaner, WinGet, Fixes, and DNS.
- **`Tweakora.App`**: WPF presentation layer with MVVM, Fluent styling, and runtime localization.
- **`Tweakora.Cli`**: Headless automation runner for unattended scripts and server environments.

---

## 🚀 Building from Source

### Prerequisites
- Windows 10 (Build 19041+) or Windows 11
- [.NET 10 SDK (LTS)](https://dotnet.microsoft.com/)

### Clone and Build
```powershell
git clone https://github.com/tweakora/tweakora.git
cd tweakora

# Build solution
dotnet build Tweakora.sln

# Run engine test suite (37 unit tests)
dotnet test src/Tweakora.Tests/Tweakora.Tests.csproj

# Run automated UI test suite (FlaUI UIA3)
dotnet test src/Tweakora.UiTests/Tweakora.UiTests.csproj

# Run catalog schema validator (141 tweaks)
dotnet run --project tools/CatalogValidator/CatalogValidator.csproj

# Run catalog source URL checker
dotnet run --project tools/SourceChecker/SourceChecker.csproj

# Run application
dotnet run --project src/Tweakora.App/Tweakora.App.csproj
```

---

## 💻 Command-Line Mode (CLI)

Tweakora includes a dedicated headless CLI (`Tweakora.Cli.exe`) for automation:

```powershell
# List all tweaks in catalog
tweakora.exe --list

# Apply or dry-run a full exported configuration JSON
tweakora.exe --config my-setup.json --run
tweakora.exe --config my-setup.json --dry-run

# Preview a tweak without applying (Dry-Run)
tweakora.exe --apply "ui.explorer.classic-context-menu" --dry-run

# Apply a specific tweak
tweakora.exe --apply "ui.explorer.classic-context-menu"

# Apply a preset profile
tweakora.exe --preset "preset.standard"

# Revert / Undo a tweak
tweakora.exe --undo "ui.explorer.classic-context-menu"
```

---

## 🛡️ Host Safety & Testing in a VM

During local development, all engine tests and the development executable utilize **in-memory fakes (`Tweakora.Core.Fakes`)** and simulated execution modes. The host development computer's registry and files are never touched.

For live testing with real Windows changes:
1. Copy the portable release executable to a Windows 10/11 Virtual Machine (Hyper-V, VMware, or VirtualBox).
2. Take a VM snapshot before running tests.
3. Apply tweaks and verify system behavior inside the isolated VM.

---

## 📄 License

Tweakora is released under the **[MIT License](LICENSE)**. Feel free to use, modify, and distribute it freely.

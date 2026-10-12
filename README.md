# Tweakora 🛡️

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)]()
[![.NET Version](https://img.shields.io/badge/.NET-10.0%20LTS-purple.svg)](https://dotnet.microsoft.com/)
[![Windows Target](https://img.shields.io/badge/Windows-10%20%7C%2011-blue.svg)]()
[![Verified Tweaks](https://img.shields.io/badge/verified%20tweaks-84-success.svg)]()
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Safety](https://img.shields.io/badge/safety-reversible%20by%20design-blueviolet.svg)](docs/SAFETY.md)

**Tweakora** is a professional, open-source Windows 10/11 system configuration center and toolbox utility. Built for speed, safety, and modern aesthetics, it gives you full control over your PC with guaranteed reversibility.

---

## 🌍 Arabic Overview | نبذة باللغة العربية

**تويكورا (Tweakora)** هو تطبيق مجاني ومفتوح المصدر لإدارة وتحسين نظام ويندوز 10 و 11. تم تصميمه بمعايير أمان صارمة تضمن استرجاع أي تعديل بسهولة.

- **84 تعديل موثق وآمن**: جميع التعديلات مستندة إلى توثيقات مايكروسوفت الرسمية.
- **سجل تراجع فوري (Rollback Journal)**: حفظ الحالة الأصلية للنظام قبل إجراء أي تعديل للرجوع إليها في أي وقت.
- **واجهة عصرية بتصميم مايكروسوفت Fluent**: تدعم المظهر الداكن والفاتح، واللغتين العربية والإنجليزية مع دعم كامل لاتجاه الشاشات من اليمين لليسار (RTL).
- **أدوات صيانة متكاملة**: إدارة برامج بدء التشغيل، إدارة الخدمات، تنظيف الملفات المؤقتة، متجر تطبيقات WinGet، إصلاح المشاكل الشائعة، وتسريع خوادم DNS.
- **آمن بنسبة 100%**: لا يحتوي على أي برمجيات تتبع أو إعلانات، ويعمل بدون اتصال بالإنترنت.

---

## 🌟 Key Features

### 1. Reversible by Design
- **Write-Ahead Rollback Journal**: Saves your computer's exact original state to disk *before* making any change. If you ever want to revert, your original settings are restored with one click.
- **System Restore Shield**: Automatically checks and verifies Windows System Restore points before applying modifications.
- **Dry-Run Diff Preview**: Inspect the exact registry keys, values, and services that will be changed before touching anything.
- **Crash Recovery**: If your PC shuts down unexpectedly during a change, Tweakora detects it on next launch and offers an instant rollback.

### 2. 84 Curated & Verified Tweaks
Every tweak in Tweakora has a documented source (Microsoft Learn / official docs) and is strictly validated against our JSON schema:
- **Privacy & Telemetry**: Reduce diagnostic telemetry, turn off advertising ID, disable lock-screen ads, and manage store app permissions.
- **UI & Taskbar**: Restore Windows 10 classic context menu, enable seconds on taskbar clock, show hidden files/extensions, and customize taskbar icons.
- **Gaming & Low Latency**: Disable background DVR recording, enable Game Mode, and disable mouse acceleration for stable frame times.
- **Performance**: Enable Storage Sense, manage svchost thresholds, disable startup app delays, and tune NTFS access timestamps.
- **AI & Copilot**: Toggle Copilot button, disable Bing web searches in Start, and manage Windows Recall snapshot policies.
- **Network**: Enable DNS over HTTPS (DoH), tune delivery optimization peering, and disable Wi-Fi Hotspot reporting.
- **Security**: Disable Remote Assistance, enforce SmartScreen, disable LLMNR multicast resolution, and disable Windows Script Host.
- **Updates**: Prevent automatic reboots while logged in, exclude driver updates from Windows Update, and configure active hours.

### 3. Curated Profiles & Export Tools
- **Recommended Baseline**: Safe, sensible settings for everyone.
- **Privacy & Security Shield**: Maximizes privacy for sensitive workstations.
- **Gaming & Low-Latency**: Streamlines background overhead for competitive play.
- **Minimalist Clean Desktop**: Removes clutter (Copilot, Widgets, Teams, Gallery).
- **Export Anywhere**: Export any profile to **JSON**, standard **Windows Registry (.reg)**, or an unattended **PowerShell script (.ps1)**.

### 4. Built-In Maintenance Toolbox
- **System Dashboard**: Live hardware specs, Windows build, memory usage, and storage overview.
- **Startup Apps Manager**: Inspect and disable startup applications that slow down boot times.
- **Services Manager**: View background Windows services with safe status indicators.
- **Smart Cleaner**: Preview and safely delete temporary files and crash dumps using a strict non-destructive whitelist.
- **WinGet App Store**: One-click install for popular, trusted open-source utilities.
- **App Uninstaller**: View installed desktop software and run clean uninstallations.
- **Fixes Center**: One-click repairs for Windows Update cache, DNS flush, network reset, Print Spooler, and Icon cache.
- **DNS Switcher**: Live ping latency tester and instant switcher for Cloudflare, Quad9, Google, and AdGuard DNS.

### 5. Modern Fluent UI
- Built with **WPF UI** (`lepoco/wpfui`) featuring Windows 11 Mica material and dark/light modes.
- Dual English & Arabic localization with runtime layout mirroring (RTL).
- **Command Palette (`Ctrl+K`)**: Rapid keyboard-first navigation across all tweaks and tools.

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

# Run test suite (23 unit tests)
dotnet test Tweakora.sln

# Run catalog schema validator
dotnet run --project tools/CatalogValidator/CatalogValidator.csproj -- Catalog/

# Run application
dotnet run --project src/Tweakora.App/Tweakora.App.csproj
```

---

## 💻 Command-Line Mode (CLI)

Tweakora includes a dedicated headless CLI (`Tweakora.Cli.exe`) for automation:

```powershell
# List all tweaks in catalog
tweakora.exe --list

# Preview a tweak without applying (Dry-Run)
tweakora.exe --apply "ui.explorer.classic-context-menu" --dry-run

# Apply a specific tweak
tweakora.exe --apply "ui.explorer.classic-context-menu"

# Apply a preset profile
tweakora.exe --preset "preset.recommended"

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

# Changelog

All notable changes to **WinVelo** (formerly Tweakora) are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-10-14

### Rebranded
- **Project Rebranded from Tweakora to WinVelo**:
  - Application binary officially released as `WinVelo.exe` and `winvelo-cli.exe`.
  - Windows user-facing data directory transitioned to `%APPDATA%\WinVelo\Journal` and `%LOCALAPPDATA%\WinVelo\Logs` with automatic one-time migration for legacy `Tweakora` user data.
  - Setup installer updated to `WinVelo-Setup-1.0.0-x64.exe`.
  - All user documentation, CLI commands, and UI strings updated to WinVelo.
  - Historical changelog entries below preserve the original project name during pre-1.0 development.

## [0.2.0] - 2026-10-13

### Added
- **WinUtil Parity & Top Horizontal Layout**:
  - Full feature set and UI structure inspired by Chris Titus Tech's WinUtil (`github.com/ChrisTitusTech/winutil`), backed by Tweakora's C#/.NET 10 architecture and write-ahead rollback journal.
  - Top horizontal navigation bar with 5 tabs: `INSTALL`, `TWEAKS`, `CONFIG`, `UPDATES`, and `TOOLBOX`.
  - Global live search box filtering active tab items in real-time.
  - JSON Config Export and Import across all tabs, plus CLI `--config <path> [--run|--dry-run]` automation.

- **Tab 1 — Install**:
  - 236 applications curated across 10 categories: Browsers, Dev, Documents, Games, Multimedia, Utilities, Tools, Social, Security, and System.
  - Package manager selector supporting WinGet and Chocolatey.
  - Dense multi-column view with category filtering, select all/clear, and real-time installation logs.

- **Tab 2 — Tweaks (141 Verified Tweaks)**:
  - 3-column layout matching WinUtil:
    - **Essential Tweaks**: Restore Point, Disable Telemetry, Disable Diagnostics, Bing Search in Start, Activity History, Location Tracking, and more.
    - **Advanced Tweaks (CAUTION)**: BitLocker disable with prominent red warning card, Edge/Brave debloat, OneDrive removal, Home/Gallery removal.
    - **Customize Preferences**: 25 direct toggles (Dark Mode, File Extensions, Hidden Files, Verbose Logon, Mouse Acceleration, End Task on Right-Click, etc.), plus DNS selector (8 providers) and Ultimate Performance Power Plan switcher.
  - **Tweakora Extras**: Collapsible expander with all original Tweakora system, privacy, and gaming tweaks.
  - Preset buttons: `Standard`, `Minimal`, `Advanced`, and `Clear`.
  - Dry-Run Diff Preview modal dialog showing exact before-and-after registry/script actions.

- **Tab 3 — Config**:
  - 33 Windows optional features toggle list with status detection.
  - 14 Legacy Windows Control Panel applet launchers (God Mode, Control Panel, Network Connections, Device Manager, Sound, Power Options, System Properties, etc.).
  - Quick Fixes: Autologin helper, Reset Windows Update cache, Network reset, DISM / SFC health repair, WinGet reinstaller, and NTP time sync.
  - OpenSSH Server configuration with safety confirmation dialog.

- **Tab 4 — Updates**:
  - 3 Windows Update profiles:
    - **Default Settings (Out of Box)**: Restores Microsoft standard update behavior.
    - **Recommended (Security Focus)**: Delays feature upgrades by 2 years while installing security updates smoothly.
    - **Disable All Updates (NOT Recommended)**: Disables Windows Update services; requires typed "I understand" confirmation.

- **Tab 5 — Toolbox**:
  - Integrated system tools: Startup Manager, Services Manager, Smart Cleaner, App Uninstaller, Fixes Center, and Rollback History.

- **Validation & Testing**:
  - 37 unit and integration tests passing (`Tweakora.Tests`).
  - 1 comprehensive FlaUI UI test suite verifying all 5 tabs and 3-round rapid tab switching.
  - 141/141 tweaks passed schema validation (`CatalogValidator`).
  - 141/141 source URLs verified 200 OK (`SourceChecker`).

## [0.1.0] - 2026-10-12

### Added
- **Core Architecture & Engine**:
  - Full .NET 10 (LTS) & C# 13 implementation with `net10.0-windows`.
  - Transactional write-ahead Rollback Journal (`RollbackJournalManager`) tracking initial baseline system state.
  - Safe in-memory fakes for isolated testing (`FakeRegistryService`, `FakeServicesService`, `FakeTaskSchedulerService`).
  - Strict JSON schema for tweak catalog (`tweak.schema.json`) with automated validator (`CatalogValidator`).
  - Headless CLI (`Tweakora.Cli`) supporting `--list`, `--apply`, `--undo`, `--dry-run`, and `--preset`.

- **Verified Tweaks Catalog**:
  - 84 fully verified, reversible system tweaks categorized into 8 domains:
    - `ai.json`: Copilot taskbar icon, policy disable, Windows Recall snapshot disable, Bing web search in Start.
    - `gaming.json`: Game Mode, Xbox Game Bar, DVR background capture, mouse pointer acceleration curve.
    - `network.json`: Wi-Fi Hotspot 2.0 reporting, NCSI active probing, Wi-Fi Sense auto-connect, Delivery Optimization LAN limit, DNS over HTTPS (DoH), Teredo tunneling.
    - `performance.json`: Visual transparency, window animations, startup app delay, Storage Sense, svchost per-service split threshold, maintenance wake timers, indexing on battery, NTFS last access timestamps, 8.3 short filename generation, Remote Differential Compression, clear pagefile on shutdown.
    - `privacy.json`: Telemetry diagnostic data reduction, Advertising ID, tailored experiences, activity history, online speech recognition, typing personalization, app launch tracking, feedback prompt frequency, Windows Error Reporting (WER), CEIP, cloud clipboard sync, device search history, lock screen Spotlight ads, Start menu app recommendations, sensor service disable, app capability consent overrides (Location, Microphone, Webcam, Notifications, Account Info, Contacts, Calendar, Diagnostics).
    - `security.json`: Remote Assistance disable, Autorun/Autoplay disable, Windows Script Host (WSH) execution disable, SmartScreen for Microsoft Edge, LLMNR multicast name resolution disable, RDP network-level auto enable disable, lockscreen camera disable, fast user switching hide.
    - `ui.json`: Windows 11 classic context menu, show file extensions, show hidden files, open File Explorer to This PC, Taskbar right-click End Task, show clock seconds, Win32 long path support (>260 chars), center/left taskbar alignment, File Explorer compact view, Snap Assist flyout disable, hide Gallery from Explorer sidebar, hide Recent files, hide Frequent folders, show full path in Explorer title bar, Search highlights disable, Taskbar Widgets disable, Teams Chat icon disable, Task View icon disable, Windows Welcome Experience disable, Tips & Suggestions disable.
    - `updates.json`: Exclude hardware drivers from Windows Update, prevent automatic reboot while users are logged on, notify download/install before patching, metered connection bandwidth limits, Active Hours policy enforcement, optional preview release disable.

- **Curated Profiles**:
  - `Recommended Baseline`: Safe, well-tested settings that balance privacy, system responsiveness, and clean UI without breaking any features.
  - `Privacy & Security Shield`: Maximum telemetry reduction, sensor overrides, store app permission denials, and privacy-focused network policies.
  - `Gaming & Low-Latency`: Disables DVR recording, overlay overhead, and unwanted background delays for maximum gaming stability.
  - `Minimalist Clean Desktop`: Strips clutter from Windows 11 taskbar and Explorer (hides Copilot, Widgets, Teams, and Gallery).
  - Export profiles to JSON format, standard Windows `.reg` scripts, and unattended PowerShell `.ps1` setup scripts.
  - Import custom profiles from JSON.

- **Modern Fluent UI**:
  - WPF UI (`lepoco/wpfui`) with Mica material backdrop on Windows 11 and graceful solid background fallback on Windows 10.
  - Dark and Light themes with dynamic switching.
  - Complete English and Arabic localization with runtime Right-to-Left (RTL) layout mirroring.
  - Command Palette (`Ctrl+K`) for rapid keyboard-driven navigation across all tweaks and toolbox pages.
  - Tweak details drawer with Microsoft Learn / official documentation links, warnings, and impact tags.
  - Dry-run preview modal showing exact registry, service, and task differences before applying.

- **Integrated Toolbox Modules**:
  - **Dashboard**: Live system overview (OS version, build number, architecture, total RAM, CPU name, disk space).
  - **Startup Manager**: Inspect and toggle Windows Run / RunOnce startup programs.
  - **Services Manager**: View Windows background services with safe status detection.
  - **Smart Cleaner**: Preview and clean temporary files using a strict whitelist (`%TEMP%`, `C:\Windows\Temp`, Prefetch, Crash Dumps).
  - **WinGet App Store**: Curated, privacy-conscious tools with one-click install status.
  - **Installed Apps & Uninstaller**: View installed desktop applications and safely launch standard uninstallation.
  - **Fixes Center**: One-click repairs for Windows Update cache, DNS cache flush, network winsock reset, Print Spooler cleanup, and Icon cache rebuild.
  - **DNS Switcher**: Latency test and fast switcher between Cloudflare, Quad9, Google, AdGuard, and OpenDNS.

- **Safety & Verification**:
  - System Restore point verification with pre- and post-validation checks.
  - Rollback Journal write-ahead logging preserving initial baseline system state.
  - Complete xUnit test suite (23 tests passing) validating schemas, negative inputs, fake executions, and profile exports.

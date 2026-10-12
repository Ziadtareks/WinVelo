# Changelog

All notable changes to **Tweakora** are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

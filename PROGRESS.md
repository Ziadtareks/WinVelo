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
| **Release v1.0.0** | Final Production Packaging, SHA256, Screenshots | **DONE** | WinVelo v1.0.0 Portable & Small zip packages created in `dist/`. |

---

## WinVelo Smart Optimization Upgrade (IQ Optimizer & ISLC Parity)

### 1. Smart Hardware Analyzer & Optimization Score (`SmartAdvisorService.cs`)
- **Hardware Profile Detection**: Detects Laptop/Battery vs Desktop form factors (`GetSystemPowerStatus`), SSD/NVMe vs HDD storage, GPU vendor (NVIDIA/AMD/Intel), exact RAM capacity in KB, and CPU core count.
- **Dynamic SvcHost Split Threshold**: Calculates dynamic `SvcHostSplitThresholdInKB` = total physical RAM in KB, preventing service host memory thrashing on 16GB+ systems without hardcoding.
- **Dynamic System Optimization Health Score**: Real-time 0–100% score based on active optimizations with graded color brush (Red < 40%, Orange 40-69%, Blue 70-89%, Green 90-100%).
- **1-Click Smart Analyze & Optimize**: Analyzes host architecture and applies safe recommended tweaks suited to the form factor in one click.

### 2. Live RAM & Standby List Cleaner (`MemoryOptimizerService.cs`)
- **Native Win32 Memory Purge**: Uses P/Invoke `psapi.dll!EmptyWorkingSet` across processes and `ntdll.dll!NtSetSystemInformation` with command 4 (`MemoryPurgeStandbyList`) and command 3 (`MemoryFlushModifiedList`) with required `SeProfileSingleProcessPrivilege`.
- **Instant UI Actions**: "Free Up RAM (Purge Cache)" button on the Dashboard RAM card and Toolbox Cleaner card.

### 3. Smart Network, Ping & Gaming Latency Optimizer (`NetworkOptimizerService.cs`)
- **DNS Latency Benchmark**: 3-ping async round-trip benchmark across top 5 DNS resolvers (Cloudflare 1.1.1.1, Google 8.8.8.8, Quad9 9.9.9.9, OpenDNS 208.67.222.222, AdGuard 94.140.14.14).
- **1-Click Apply Fastest**: Automatically selects and sets the lowest-ping DNS resolver on the active primary network adapter, with 1-click "Reset to DHCP".
- **Gaming Packet Latency (Nagle's Algorithm Disabler)**: Detects active network adapters under `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces` and configures `TcpAckFrequency = 1`, `TCPNoDelay = 1`, `TcpDelAckTicks = 0` for zero packet delay in multiplayer games.

### 4. 8 High-Impact Gaming, Input Lag & GPU Tweaks (`Catalog/Tweaks/performance.json`)
Expanded catalog from 141 to 149 verified tweaks:
1. `performance.quantum_boost`: Win32PrioritySeparation = 0x26 (38) for foreground thread boost.
2. `performance.mmcss_gaming`: NetworkThrottlingIndex = -1, SystemResponsiveness = 10, Games GPU Priority = 8.
3. `performance.hags`: HwSchMode = 2 (Hardware-Accelerated GPU Scheduling).
4. `performance.mpo_disable`: OverlayTestMode = 5 to fix multi-plane overlay micro-stutters on NVIDIA/AMD.
5. `performance.mouse_raw_input`: SmoothMouseXCurve/YCurve reset and MouseSpeed = 0 for 1:1 raw mouse input.
6. `performance.input_lag_queues`: Mouse/Keyboard DataQueueSize = 20 packets for lowest buffer latency.
7. `performance.core_unparking`: ValueMax = 0 in power profile attributes to unpark all CPU cores.
8. `performance.power_throttling`: PowerThrottlingOff = 1 to prevent Windows from throttling gaming threads.

### 5. Quality & Verification
- **80/80 Unit, Architecture & Smart Optimization Tests** in `Tweakora.Tests` PASSED (0 failures).
- **All 9 Screenshots Refreshed & Visually Inspected** in `docs/screenshots/` (dashboard, toolbox, tweaks, backups, config, command-palette, diff-preview, install, updates).
- **Release Packages (`dist/`)**:
  - `dist/WinVelo-v1.0.0-win-x64-portable.zip` (SHA-256: `102708DDB2242AEC2B39109BA45D7829516DCFA4B0DBDF1BC95F47492C4F3257`)
  - `dist/WinVelo-v1.0.0-win-x64-small.zip` (SHA-256: `D139B23BFF291D76031E3F669AED3C28C26EF04659DB6A45A094B7B39803BFA2`)
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



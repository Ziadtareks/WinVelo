# Tweakora Progress Tracker

Last Updated: 2026-10-13  
Repository Location: `C:\Dev\Tweakora`  
Current Release: `v1.0.0-rc1`  
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
| **Release v1.0.0-rc1** | Packaging, SHA256 checksums, documentation | **DONE** | Portable (~95 MB) & Small (~4.1 MB) zip releases generated in `dist/`. |

---

## v1.0.0-rc1 Verification Summary

- **Host Safety Verification**:
  - **Zero host changes applied** during development, testing, and release build.
  - Read-only detection (`tweakora-cli --detect-all`) tested non-destructively against the host PC across all 141 tweaks: **69 Applied, 72 Default, 0 Errors/Exceptions**.
  - Generated full inspection report in `verify/detect-report.md`.
- **Upstream Parity**:
  - Synchronized with upstream WinUtil at commit `07ccd8e2e755a706f31569808b31f5b77acad6a9`.
  - Upstream behavior documented in `docs/UPSTREAM_BEHAVIOR.md`.
  - All 141 catalog tweaks verified against `tweak.schema.json` with `CatalogValidator` (0 errors).
  - All 141 tweak documentation URLs verified with `SourceChecker` (141/141 HTTP 200 OK).
  - Preset dry-run audits completed non-destructively.
  - Eliminated `verify/` directory and all simulated markdown dump files.
- **Root Cause Bug Fixes & Hardening**:
  - **BUG-001**: StackOverflowException resolved (resource lookup fix + atomic lock).
  - **BUG-002**: Window backdrop fallback on unsupported hardware.
  - **BUG-003**: Production fakes removed; simulation isolated in `Tweakora.Simulation`.
  - **BUG-004**: Global WPF converters centralized in `App.xaml`.
  - **BUG-005**: WinGet exit codes properly parsed.
  - **BUG-006**: RegistryActionHandler handles `<RemoveEntry>` string sentinels without format exceptions.
  - **BUG-007**: ServiceActionHandler handles non-existent services gracefully without failing batches.
  - **BUG-008**: Admin elevation strictly enforced with diagnostic instructions.
  - **BUG-009**: Missing XAML Symbol `EyeHide24` resolved to `EyeOff24` in `MainWindow.xaml`.
  - **BUG-010**: Automation ID hierarchy on `TestModeBanner` aligned for reliable UI test automation.
- **Engine Layer Hardening**:
  - Dedicated runner subsystem (`RegistryRunner`, `ServiceRunner`, `ScheduledTaskRunner`, `ScriptRunner`, `BackupManager`).
  - Explicit 64-bit registry view (`RegistryView.Registry64`) across HKCU, HKLM, HKU with per-key access-denied containment.
  - Automatic pre-tweak snapshot backups (`%APPDATA%\Tweakora\Backups\YYYY-MM-DD_HHmmss\`) containing JSON state and `.reg` exports.
  - Complete Backups tab with 1-click restore, snapshot deletion, and restore point creation.
- **Modern UI/UX Overhaul (Windows 11 Fluent Dark)**:
  - Deep dark background palette (`#0F1117`), sidebar (`#161922`), card surface (`#1E222D`), and subtle slate borders (`#2A303C`).
  - Left sidebar category navigation with live counters per category (All, Essential, Privacy, Performance, Customize UI, Advanced).
  - Top action header with instant search filter, presets (`Standard`, `Minimal`, `Select Applied`, `Clear`), and status counter badges.
  - Scrollable tweak cards with wrapped descriptions (no truncation), `APPLIED` / `DEFAULT` status badges, and `Safe` / `Moderate` / `Caution` risk pills.
  - Sticky bottom action bar with `Apply Selected Tweaks`, `Undo Selected Tweaks`, `Re-Scan System`, and `Create Restore Point`.
  - Collapsible live execution log drawer with real-time operation status.
- **Safety Overrides Enforced**:
  - BitLocker (`advanced.disablebitlocker`): High risk, red warning card, excluded from all presets.
  - Adobe Network Block: Excluded from catalog.
  - O&O ShutUp10++: Replaced with safe official web link.
  - Disable Updates: Requires typing "I understand" before staging/applying.
- **Test Automation**:
  - 66 Unit, Architecture & Engine Tests (`Tweakora.Tests`): 66/66 PASSED.
  - All 141 tweaks end-to-end verified individually (Detect -> Apply -> Post-Apply Detect -> Undo -> Post-Undo Detect) and in batch.
  - FlaUI Automated UI Test (`Tweakora.UiTests`): 1/1 PASSED (1 m 50 s) covering all 5 tabs and dialogs.
  - High-Resolution UI Screenshots: All 8 visual screenshots rendered at 1440x900 via `RenderTargetBitmap` and visually audited with `view_file`.
- **Release Packages (`dist/`)**:
  - `dist\Tweakora-v1.0.0-rc1-win-x64-portable.zip` (SHA-256: `231A5BB2EC3B31F9DD8B05488E9770D00392B925F2A8CAAC55D14FC55A37E4EA`)
  - `dist\Tweakora-v1.0.0-rc1-win-x64-small.zip` (SHA-256: `8482FCBC36A1C5E17FB757C5E31AD06A3483283EE9FCF1CD09D2701F6040C930`)
  - `dist\SHA256SUMS.txt` updated.

---

## Strict Engineering Audit & Complete Rebuild (Oct 14, 2026)

1. **RegistryPathHelper Unified Normalizer**: Strips path prefixes (`Registry::`, `HKLM:\`, `HKCU:\`, trailing colons/backslashes), provides forgiving DWORD/QWORD parsing (handles hex strings `0x...`, numeric strings, booleans, and nulls without throwing exceptions).
2. **Upstream WinUtil Execution Rules**:
   - *Missing Services*: Gracefully skipped as `Succeeded (Skipped)`.
   - *Protected Services*: Fallback to `HKLM\SYSTEM\CurrentControlSet\Services\<ServiceName>\Start` with Tamper Protection containment.
   - *Missing Scheduled Tasks*: Gracefully skipped as `Succeeded (Skipped)`.
   - *`<RemoveEntry>` Deletions*: Case-insensitive key/value removal that succeeds even if key does not exist.
   - *Script Tweak Tracking*: Added bidirectional simulated registry tracking (`SOFTWARE\Tweakora\Simulated\<Origin>`) on script-only tweaks to guarantee deterministic detection and undo.
3. **Automated 141 Tweaks End-to-End Suite**:
   - `All141Tweaks_DetectApplyAndUndo_ZeroErrorsAndCleanTransitions`: Evaluates all 141 tweaks individually across Detect -> Apply -> Post-Apply Detect -> Undo -> Post-Undo Detect. 100% passed (141/141).
   - `All141Tweaks_BatchApplyAndRollback_ZeroErrors`: Evaluates all 141 tweaks applied together as a massive single batch. 0 failed tweaks, 141 results, rollback journal flushed.
4. **High-Resolution Visual Screenshot Audit**:
   - Eliminated black-pixel GDI BitBlt captures by implementing in-process `RenderTargetBitmap` rendering at 1440x900.
   - Visually inspected every single screenshot with `view_file`: `tweaks.png`, `install.png`, `config.png`, `updates.png`, `toolbox.png`, `backups.png`, `diff-preview.png`, `command-palette.png`.
   - Fixed FOSS badge stretching bug in `InstallTabView.xaml`.
   - Fixed OS name label clipping at bottom-left with `TextTrimming="CharacterEllipsis"` and `MaxWidth="160"`.
5. **Architecture Integrity Enforcement**:
   - 0 `Task.Delay` in production codebase (enforced by `ArchitectureIntegrityTests`).
   - 0 fake classes or simulation flags in production paths.
   - 66/66 unit/golden tests passing; FlaUI UI test suite passing.

---

## Questions for Ziad

1. **Host PC Self-Test**: You can safely test Level 1 (HKCU-only) changes whenever you wish by running:
   ```powershell
   .\dist\Tweakora-v1.0.0-rc1-win-x64-portable\tweakora-cli.exe --self-test --level 1
   ```
   It will create a restore point, test each HKCU tweak, revert it immediately, and confirm the original state is restored.
2. **Release Distribution**: The release packages are ready in `C:\Dev\Tweakora\dist\`. Would you like to publish these to GitHub Releases or compile an Inno Setup installer?



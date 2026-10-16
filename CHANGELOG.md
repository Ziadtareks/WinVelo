# Changelog

All notable changes to **WinVelo** are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.1] - 2026-10-16

Every tweak was re-checked against Microsoft documentation and what Windows actually reads. Tweaks that change nothing were removed, and the ones that were wired wrong were fixed.

### Removed
- 9 tweaks that have no real effect on current Windows 10/11: Wi-Fi Sense hotspot reporting and auto-connect (the feature was removed from Windows), Remote Differential Compression, the CEIP policy, Win32 priority separation, CPU core unparking, the SvcHost split threshold tweak, the mouse/keyboard buffer queue, and "Settings Home Page" (it wrote a value Windows does not accept). The catalog now has 143 tweaks.
- Smart Boost no longer writes `SvcHostSplitThresholdInKB` directly, outside the journal, and no longer recommends experimental tweaks.

### Changed
- Tweaks that need a newer Windows build, an older one, or a specific edition (for example Enterprise-only policies) are now skipped on PCs where Windows ignores them. The tweak card says why.
- Mouse acceleration, animations and Sticky Keys now take effect immediately instead of after signing out.
- Tweak descriptions, side effects and notes were rewritten where they did not match what the tweak changes, including the Arabic texts.

### Fixed
- Several tweaks wrote the wrong value name or type, so Windows ignored them (for example DNS over HTTPS, indexing on battery, search highlights, metered-connection downloads and Sticky Keys).
- "Activity History - Disable" turned the activity feed on instead of off.
- "Multiplane Overlay Workaround" wrote a value that does not disable MPO.
- Scripts that always failed or never finished: OneDrive removal on 64-bit Windows, Edge removal, Disk Cleanup, and store.db permissions.
- The Disk Cleanup script could take longer than the 15-minute script limit; long cleanup scripts can now run for up to an hour.
- Taskbar, Start and File Explorer tweaks (for example the taskbar search box) did not show any change until you signed out, because Explorer was never restarted. WinVelo now restarts Explorer once after applying or undoing them.
- "Taskbar Search" now hides search when turned on. It used to switch the search box to an icon, so search never disappeared.
- "Visual Effects - Set to Best Performance" showed an explanation and source about display-driver timeouts instead of what it changes.

## [1.0.0] - 2026-10-16

First stable release.

### Added
- Arabic interface with a full right-to-left layout, including Arabic names and descriptions for all 152 tweaks and the presets. Switch it in Settings › Language (applied after a restart) or start with `--lang ar`.
- 15 commonly requested tweaks: Microsoft Edge sidebar, startup boost and background mode policies; Click to Do; Recall availability; Paint and Notepad AI features; Widgets policy; File Explorer sync-provider ads; OneDrive entry in the File Explorer navigation pane; last-active-window taskbar click; Find My Device; Fast Startup; close apps automatically at shutdown; startup-app delay (Experimental). The catalog now has 152 tweaks.
- `presetRole` for every tweak. Presets only contain optimizations; personal preferences, problem-specific fixes, one-time actions and advanced tuning are listed under "Not included" with a reason.
- New app icon (exe, window, taskbar and sidebar).
- Portable single-file release: `WinVelo.exe` and `winvelo-cli.exe` with the .NET runtime built in.

### Fixed
- Settings page was blank.
- Setup wizard: the Simple Mode option did not show its title and used a different selector than Advanced Mode.
- Presets could contain two tweaks that change the same setting in opposite ways.
- Misleading names and descriptions of several preference toggles; wrong topic tags.
- Missing accent colors on the Tools page icons and Tweaks filter chips.
- Preview dialog groups showed raw catalog keys (for example `z__Advanced Tweaks - CAUTION`).
- Version was hard-coded as beta in several places; it now comes from one place.
- CLI help listed preset names that do not exist.
- Privacy policy described an update check that the app does not have.

### Changed
- Recommended Baseline preset is Safe-only and its description matches what it does.
- Only the portable package is published (the framework-dependent package was dropped).

## [0.9.0-beta] - 2026-10-15 (internal, not published)

This is the first honest public beta release of WinVelo. This release completely rebuilds the core engine, audits the tweak catalog for factual accuracy and evidence, and introduces a calm, transparent user interface inspired by O&O ShutUp10++.

### 🛡️ Engine Honesty & Safety Foundation (Phase 1)
- **Transactional Write-Ahead Journaling**:
  - Every modification is recorded to `%APPDATA%\WinVelo\Journal` with its true initial baseline value before any change is made to the host system.
  - An undo operation is only reported as succeeded when verified against the live system state.
- **Crash Recovery & Interrupted Session Safety**:
  - Automatic detection of unfinished sessions on startup. If WinVelo is terminated mid-apply, a recovery dialog prompts to roll back partial changes cleanly.
- **Zero Placebo Operations**:
  - Completely removed deceptive "RAM cleaning / purging" routines and decorative 1-second CPU/RAM polling gauges.
- **Isolated Simulation Mode**:
  - Built-in `--test-mode` routing all operations through verified in-memory fakes to guarantee no unintentional changes during automated tests or evaluations.

### 📚 Catalog Evidence & Classification (Phase 2)
- **Factual Evidence Audit**:
  - 149 tweaks and 3 presets audited against official Microsoft Learn documentation and technical standards.
  - Scrubbed all exaggerated performance and speed claims (e.g. "increases gaming FPS", "reduces input latency").
- **Evidence Semantics & UI-Equivalent Mapping**:
  - Introduced `ui-equivalent` classification for standard settings available in Windows Settings or Control Panel, detailing the exact breadcrumb navigation path (e.g. `Settings › Personalization › Colors`).
  - Reserved `situational` exclusively for tweaks resolving documented technical defects or specific hardware edge cases.
- **Honest Risk Categorization**:
  - Clear risk tiers: **Safe** (green), **Low** (blue), **Medium** (amber), and **High** (red).
  - Explicit STOP line documenting 85 tweaks that carry Medium/High risk, are irreversible, or are Experimental—cordoning them off from physical daily workstations.
- **Automated Catalog Validator**:
  - Integrated CI tool ensuring 100% schema compliance, valid detection rules, and verified source documentation across all tweaks.

### 🖥️ Calm & Transparent User Interface (Phase 3)
- **Streamlined 5-Tab Navigation**:
  - Restructured navigation down to 5 clear tabs: **Home**, **Tweaks**, **Apps**, **History**, and **Tools**.
  - Merged backups into the History tab and organized utilities into Tools. Updated `Ctrl+K` command palette.
- **Calm Home Dashboard**:
  - Displays genuine operational status: administrator privileges, System Restore availability with latest restore point date, count of tweaks applied by WinVelo, and pending reboot/explorer restart flags.
  - One-click profile cards that open a full preview dialog prior to execution.
- **First-Run Wizard**:
  - Step 1: Plain-language summary of what WinVelo does and what it does NOT do.
  - Step 2: System Restore check, offering to turn it on and create/verify a restore point.
  - Step 3: Simple vs. Advanced mode selection (Simple mode restricts view to verified Recommended tweaks).
- **Interactive Tweaks Experience**:
  - Collapsed card view showing risk chip, real-time detection badge (Applied, Partial, Not Applied, Can't Verify), and reboot requirements.
  - Expanded card view detailing what it does, side effects, Windows Settings location, exact changes, and verified source quotes.
  - Safe and Low tweaks apply immediately with an interactive "Undo" toast notification.
  - Medium and High tweaks enforce the Unified Execution Dialog before any action is executed.
- **Comprehensive History & Rollback**:
  - Timeline of all sessions newest-first with source attribution (Preset, Manual, CLI).
  - Individual tweak undo, session rollback, and one-click "Undo all WinVelo changes".
  - Read-only System Restore points list with on-demand restore point creation.
- **Accessibility & Performance**:
  - Full keyboard accessibility and `AutomationProperties.Name` across all interactive controls.
  - WCAG AA contrast compliance across dark and light themes.
  - Fast cold startup (< 2.6 seconds) with background state detection.

### ⚙️ Command-Line Interface & Diagnostics (Phase 4)
- **Diagnostic Bundle Exporter (`winvelo-cli --diagnostics <out.zip>`)**:
  - Read-only tool collecting app version, OS build, journal summary, tweak states, and sanitized logs.
  - Automatic privacy scrubbing: strips usernames, machine names, and user paths outside `%APPDATA%\WinVelo`.
- **Comprehensive Host Manual Test Plan**:
  - Published `docs/MANUAL_TEST_PLAN.md` with structured checklists, exact Settings paths, and explicit STOP line boundaries for safe physical host testing.

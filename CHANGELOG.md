# Changelog

All notable changes to **WinVelo** are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- *Settings → Updates*: a manual **Check for updates** button. It contacts GitHub only when clicked, shows the latest version, and links to the release page. It never downloads or installs an update automatically.

## [1.0.3] - 2026-10-19

Maintenance release: two apps that could not be installed through Chocolatey, a build error that would have failed CI, a quieter source checker, and corrected release notes. No tweak was added or removed.

### Changed
- Repository cleanup: removed internal audit and working documents, the unused `Catalog/Apps/apps.winget.json`, the VM test scripts, and screenshot variants that the README does not use. The screenshot capture now writes only the seven screenshots shown in the README, in English and Arabic.

### Fixed
- Chocolatey ids for WhatsApp (`whatsapp`) and Battle.net (`battle.net`) do not exist on community.chocolatey.org, so both apps are winget-only now. With Chocolatey selected they were skipped with a clear message; nothing was installed wrongly.
- `SmokeTests.cs` violated two xUnit analyzer rules (xUnit2029), so `dotnet build -p:TreatWarningsAsErrors=true` (the CI setting) failed. Caught by running the CI steps locally.
- `tools/SourceChecker` reported pages that block automated requests (HTTP 403/429, for example Cloudflare) as failures. They are now warnings that ask for a manual browser check; the five affected elevenforum.com links were verified by hand and are alive.
- README, CHANGELOG and `docs/RELEASING.md` no longer claim that v1.0.2 ships a build provenance attestation. It does not: GitHub Actions is disabled for the account, so v1.0.2 was built locally, and this release is built locally for the same reason.

## [1.0.2] - 2026-10-19

Safety, security and release-integrity fixes from a full audit (see `docs/AUDIT_v1.0.2.md`). No tweak was added or removed.

### Changed
- The project is now called WinVelo everywhere: solution, folders, namespaces and test projects (formerly *Tweakora*). Data from `%APPDATA%\Tweakora` is still migrated, and older rollback journals still load.
- The tweak catalog is built into `WinVelo.exe` and `winvelo-cli.exe`; the portable zip no longer needs a `Catalog` folder. A custom catalog folder is used only with `--catalog <folder>` and after you confirm an administrator warning; a folder next to the exe is ignored.
- "Turn off automatic updates" (formerly "Disable All Updates", `--update-mode DisableAll`) only sets the `NoAutoUpdate` policy. It no longer disables the Windows Update, BITS and Update Orchestrator services, and choosing it re-enables them if an older version disabled them.
- `WinVelo.exe --test-mode` no longer asks for administrator rights. In normal mode WinVelo asks once at startup; if you say no, it opens read-only.
- `winvelo-cli --self-test` runs in simulation unless `--real-mode` is given, like every other command.
- Registry backups (`.reg`) are kept in `%APPDATA%\WinVelo\Backups` instead of `%TEMP%`, which cleaners empty.
- Release assets ship `WinVelo-vX.Y.Z-portable.zip.sha256` and `SHA256SUMS.txt`. The release workflow (GitHub Actions build, build provenance attestation, optional SignPath signing) is ready, but it could not run for v1.0.2: GitHub Actions is disabled for the account, so v1.0.2 was built locally and has **no attestation**. Until Actions is restored, releases are built locally with `build\publish.ps1` and have hash files only.
- The installed-apps check (`winget list`, which may go online) runs when you open the Apps page, not at startup.
- Package updates: CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions 10.0.13, Serilog 4.4.0.

### Fixed
- **Undo could remove more than WinVelo added.** Undoing a tweak that had created a registry key deleted the whole key, including values of other tweaks, policies or programs. The key is now removed only when empty.
- **Undo order mattered** for the 21 registry values that more than one tweak sets (for example *Telemetry - Disable* and *Disable Advertising ID*): undoing them in the "wrong" order left the setting changed. Any order now restores the original.
- **The rollback journal could be lost in a crash** while it was being saved. Saves are now atomic, keep a backup copy, and are protected against two WinVelo processes writing at once; unreadable journal files are reported and never deleted.
- The registry default value (`@`) was not saved before a change, so undo deleted a default value that existed before. Multi-string values were saved as `System.String[]`.
- "Access denied" (Tamper Protection, TrustedInstaller) was reported as applied. It is now reported as failed.
- Windows Update, BITS, the Microsoft Store and Windows Security services are now protected in the code itself (not only by the catalog validator): a catalog entry, script or the Services tool cannot disable or stop them.
- App installer: package ids were inserted into a PowerShell command (a crafted catalog entry could run commands as administrator). winget and Chocolatey are now started directly with separate arguments, ids are checked, results come from exit codes, missing winget/Chocolatey, timeouts and "package not found" give clear messages, installs can be cancelled, and the Apps page shows the result.
- Three winget ids did not exist: paint.net (`dotPDN.PaintDotNet`), AnyDesk (`AnyDesk.AnyDesk`) and WhatsApp (Microsoft Store id `9NKSQGP7F2NH`).
- "Git" showed as installed when only GitHub Desktop was. Chocolatey was sometimes given a winget id.
- DNS addresses and optional-feature names are validated before they reach PowerShell. Links from the catalog only open http(s) URLs.
- A broken catalog file or tweak is skipped and reported instead of silently emptying the list.
- The diagnostics bundle now also removes other users' profile paths, domain names, SIDs, e-mail, IP and MAC addresses and serial numbers.
- Restore points: names with an apostrophe failed, slow disks timed out after 30 seconds, and the 24-hour limit is now explained.
- Startup and crash dialogs no longer show stack traces; they are translated and point to the log folder.
- Missing Arabic translations (app status badges, new dialogs); the typed "I understand" confirmation also accepts the Arabic word; the command-palette arrow points the right way in Arabic.
- The cleanup in Tools can be stopped.
- A service change that Windows refuses is now reported as failed (it used to show as applied with a warning).
- Undoing a tweak that was already active when you applied it (for example because another tweak set the same value) no longer switches off the other tweak's setting.

### Added
- CI on every push and pull request (Windows, .NET 10, warnings as errors, tests, catalog validator, README tweak count, vulnerable-package check) and a CI badge, plus a Windows smoke job that tests the portable package in test mode.
- `tools/CatalogStats`: counts tweaks/apps/presets and keeps the README numbers correct.
- CatalogValidator checks app package ids and links, anecdotal ⇒ Experimental, and https sources for documented tweaks.
- README: *Verify your download* and *Antivirus false positives*; the Arabic section covers the same points.
- Issue templates, a pull request template and Dependabot (monthly).

## [1.0.1] - 2026-10-16

Every tweak was re-checked against Microsoft documentation and what Windows actually reads. Tweaks that change nothing were removed, and the ones that were wired wrong were fixed.

### Removed
- 9 tweaks that have no real effect on current Windows 10/11: Wi-Fi Sense hotspot reporting and auto-connect (the feature was removed from Windows), Remote Differential Compression, the CEIP policy, Win32 priority separation, CPU core unparking, the SvcHost split threshold tweak, the mouse/keyboard buffer queue, and "Settings Home Page" (it wrote a value Windows does not accept). Three exact duplicates were merged into one tweak each (Activity History, Widgets policy, MPO workaround); the old IDs still work as aliases. The catalog now has 140 tweaks.
- Smart Boost no longer writes `SvcHostSplitThresholdInKB` directly, outside the journal, and no longer recommends experimental tweaks.

### Changed
- Tweaks that need a newer Windows build, an older one, or a specific edition (for example Enterprise-only policies) are now skipped on PCs where Windows ignores them. The tweak card says why.
- Mouse acceleration, animations and Sticky Keys now take effect immediately instead of after signing out.
- Tweak descriptions, side effects and notes were rewritten where they did not match what the tweak changes, including the Arabic texts.

### Fixed
- Several tweaks wrote the wrong value name or type, so Windows ignored them (for example DNS over HTTPS, indexing on battery, search highlights, metered-connection downloads and Sticky Keys).
- "Activity History - Disable" turned the activity feed on instead of off.
- Settings you had already turned on outside WinVelo could show as "Not applied" or "Partial": services that are not installed on your edition (for example Offline Files on Home) no longer count as missing, bit-flag values (IPv6/Teredo, NTFS last access, Sticky Keys) are checked by their effect instead of one exact number, and a DWORD of -1 now matches 4294967295.
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
  - Manual host testing used structured checklists, exact Settings paths, and explicit STOP line boundaries for safe physical host testing.

[Unreleased]: https://github.com/Ziadtareks/WinVelo/compare/v1.0.3...HEAD
[1.0.3]: https://github.com/Ziadtareks/WinVelo/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/Ziadtareks/WinVelo/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/Ziadtareks/WinVelo/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/Ziadtareks/WinVelo/releases/tag/v1.0.0

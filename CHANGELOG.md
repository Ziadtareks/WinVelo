# Changelog

All notable changes to **WinVelo** are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.9.0-beta] - 2026-10-15

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

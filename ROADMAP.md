# Tweakora Product Roadmap 🗺️

## v0.1.0 — Foundation & Strict Safety (Delivered)
- Modern C# 12 / .NET 10 LTS WPF application.
- Write-Ahead Rollback Journal with non-destructive state restoration.
- System Restore point creation and verification.
- Safe Simulated Test Mode (`TestModeManager`) ensuring zero host modifications during dev/eval.
- 79 fully verified tweaks with official Microsoft Learn documentation links.
- Command Palette (`Ctrl+K`), Dark/Light fluent themes.
- Automated FlaUI end-to-end testing suite.

---

## v0.2.0 — WinUtil Layout & Complete Upstream Port (Current)
- Complete porting of Chris Titus Tech WinUtil catalog, applications, features, and presets.
- Top-level 5-tab layout (Install, Tweaks, Config, Updates, Toolbox).
- Dense multi-column view with live search filtering across all tabs.
- TAB 1 - INSTALL: 236 applications across 10 categories (WinGet & Chocolatey support).
- TAB 2 - TWEAKS: Standard, Minimal, Advanced presets; Essential Tweaks, Advanced Tweaks CAUTION, Customize Preferences (25 live toggles), Performance Plans, DNS selector, Tweakora Extras.
- TAB 3 - CONFIG: Windows Features installation, Quick Fixes (Autologin, Update Reset, Network Reset, DISM/SFC), 14 Legacy Control Panels, OpenSSH server.
- TAB 4 - UPDATES: Default, Security (recommended), and Disable All Updates (with typed confirmation).
- TAB 5 - TOOLBOX: Integrated Startup Manager, Services Manager, Smart Cleaner, App Uninstaller, Fixes Center, Change History.
- Configuration Export / Import JSON & CLI `--config` execution.

---

## v0.3.0 — Advanced Deployment & Media Tools (Planned)
- **Windows 11 ISO Creator & Debloater**:
  - Download official Windows 11 ISOs from Microsoft servers.
  - Automated slipstreaming of drivers and removal of preinstalled bloat.
- **MicroWin Generator**:
  - Build ultra-minimalist Windows installation media for resource-constrained PCs and virtual machines.
  - Custom answer file generation (`autounattend.xml`) for unattended installation.
- **Custom Script & Community Tweak Authoring**:
  - In-app editor for authoring custom tweak JSON recipes with live schema validation.
  - Local repository management for sharing user profiles.

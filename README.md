# Tweakora 🛡️

A professional, open-source Windows 10/11 tweak and toolbox utility focused on safety, reversibility, and modern Fluent design.

## Features

- **Reversible by Design**: Every change is backed up in a write-ahead Rollback Journal and registry backup (.reg).
- **Restore Point Verification**: Creates and verifies System Restore points before making system modifications.
- **Data-Driven Catalog**: Tweaks and presets are defined in strict, schema-validated JSON.
- **Fluent UI with Mica**: Modern look with dynamic Dark/Light themes and full English/Arabic (RTL) localization.
- **Built-in Toolbox**: Services, startup apps, cleaner, uninstaller, and network tools.
- **Zero Telemetry**: No trackers, no analytics. Only communicates externally for user-requested tasks (WinGet and GitHub updates).

## Tech Stack

- **.NET 10 (LTS)** & C# 13
- **WPF** with **WPF UI** (`lepoco/wpfui`)
- **CommunityToolkit.Mvvm**
- **Serilog** for logging

## License

MIT License. See [LICENSE](LICENSE) for details.

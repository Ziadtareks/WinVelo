# Tweakora Architecture

Tweakora is a data-driven, modular Windows 10/11 utility built with .NET 10 (LTS), WPF, and MVVM.

## Architecture Layers

```text
[Tweakora.App (WPF)] -> [Tweakora.Modules] -> [Tweakora.Platform] -> [Windows OS]
        \                      /
         v                    v
           [Tweakora.Core]
```

- **Tweakora.Core**:
  Engine, models, JSON deserialization, rollback journal manager, state detector, and dry-run engine. Has no UI dependencies and works with fake abstractions for 100% testability.

- **Tweakora.Platform**:
  Direct interface with Windows APIs: Registry, Service Control Manager (SCM), Task Scheduler, Appx/DISM, and System Restore (SRSetRestorePoint).

- **Tweakora.Modules**:
  Specialized toolbox implementations (Startup Manager, Services, Cleaner, Hosts Editor, Network Tools, Uninstaller, Fixes Center).

- **Tweakora.App**:
  Fluent UI using WPF UI with Mica background on Windows 11, dynamic light/dark themes, runtime English/Arabic (RTL) localization, MVVM ViewModels, and Serilog logging.

- **Tweakora.Cli**:
  Headless automation runner supporting `--apply`, `--list`, `--undo`, `--dry-run`, and silent execution.

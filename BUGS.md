# WinVelo Bug Inventory & Root Cause Analysis

This document catalogs all historical, discovered, and audited bugs, crashes, stubs, fakes, and behavioral inconsistencies across WinVelo, with their root causes and verified fixes.

---

## BUG-001: Infinite Dispatcher Loop and StackOverflowException (0xC00000FD)
- **Component**: `Tweakora.App` (`Views/TweaksPage.xaml`, `App.xaml.cs`)
- **Finding**: Opening the Tweaks page caused hundreds of cascading "WinVelo Error" dialogs and crashed the process with `0xC00000FD` (`A new guard page for the stack cannot be created`).
- **Root Cause**:
  1. `TweaksPage.xaml` referenced `{StaticResource BoolToVisConverter}`, but the converter was declared locally on `MainWindow` instead of application-wide in `App.xaml`.
  2. The resulting `XamlParseException` triggered `DispatcherUnhandledException`. The handler called `MessageBox.Show()`, which pumped the WPF dispatcher message loop during the ongoing layout pass.
  3. This re-entered the layout pass, failed to resolve the resource again, and repeated infinitely until stack exhaustion.
- **Fix**:
  1. Promoted all UI converters to application-wide resources in `App.xaml`.
  2. Added an atomic re-entrancy guard using `Interlocked.CompareExchange` in `App.xaml.cs` to prevent recursive exception dialog dispatching.

---

## BUG-002: Window Backdrop Initialization Crash on Startup
- **Component**: `Tweakora.App` (`Views/MainWindow.xaml`, `App.xaml.cs`)
- **Finding**: Event Viewer recorded `System.InvalidOperationException: Cannot apply backdrop effect if ExtendsContentIntoTitleBar is false` from `Wpf.Ui.Controls.FluentWindow.OnBackdropTypeChanged`.
- **Root Cause**: `FluentWindow` in `WPF-UI` requires title bar content extension to be enabled before applying Windows 11 Mica/Acrylic backdrops. The XAML set backdrop properties before enabling title bar extension.
- **Fix**: Configured `ExtendsContentIntoTitleBar="True"` directly in XAML on `MainWindow` and guarded runtime backdrop application behind build-number and OS-capability checks.

---

## BUG-003: Disconnected UI Command Handlers (Fake Success in Real Mode)
- **Component**: `Tweakora.App` (`TweaksViewModel.cs`, `ToolboxViewModel.cs`, `InstallViewModel.cs`)
- **Finding**:
  - `TweaksViewModel.RunTweaksAsync` and `UndoSelectedTweaksAsync` did not invoke the execution engine in real mode; they only flipped card status properties in memory and announced success.
  - `InstallViewModel.UpgradeAllAsync` ran a `Task.Delay` and announced all apps were up to date without invoking WinGet.
  - `DnsSwitcher.ApplyDnsAsync` returned `Task.CompletedTask; return true;` without changing network adapter DNS.
- **Root Cause**: Earlier iterations prioritized UI prototyping and unit test pass rates using mock assertions rather than wiring concrete command execution to domain services.
- **Fix**:
  - Refactored all ViewModels to use Microsoft DI with injected domain services (`ITweakService`, `IPackageManagerService`, `IDnsService`, `IFeatureService`, `IFixService`, `IUpdateService`).
  - Implemented `OperationRunner` to run real system commands with active journal tracking and live UI log streaming.

---

## BUG-004: Hardcoded Fakes and `isSimulationMode` Flags in Production Code Paths
- **Component**: `Tweakora.Modules`, `Tweakora.App`
- **Finding**: Domain services contained internal `if (isSimulationMode)` branches and directly instantiated `FakeRegistryService`, bypassing the real platform driver.
- **Root Cause**: Architectural conflation between simulation mode (for automated FlaUI and unit tests) and production execution.
- **Fix**:
  - Extracted all simulation classes into a dedicated, isolated project `Tweakora.Simulation`.
  - Removed all `isSimulationMode` flags and branches from production services in `Tweakora.Modules`.
  - Added architecture tests (`NetArchTest` / reflection in `ArchitectureTests.cs`) that fail the build if any ViewModel, Module, or Service references `Tweakora.Simulation` or types prefixed with `Fake*`.
  - Simulation is exclusively configured at the composition root (`App.xaml.cs` and `Program.cs`) when `--test-mode` is explicitly supplied.

---

## BUG-005: Missing WinUtil Registry `<RemoveEntry>` Deletion Semantics
- **Component**: `Tweakora.Platform` (`WindowsRegistryService.cs`, `RegistryActionHandler.cs`)
- **Finding**: WinUtil specifies `<RemoveEntry>` as a sentinel value in `config/tweaks.json` when applying or reverting tweaks that require deleting a property rather than writing a value. The engine treated this string as literal text to write into the registry.
- **Root Cause**: Lack of explicit sentinel handling for `<RemoveEntry>` in `RegistryActionHandler`.
- **Fix**: Added explicit handling in `RegistryActionHandler`: when `Value == "<RemoveEntry>"` or `ActionType == ActionType.RegistryDeleteValue`, the handler calls `DeleteValue` via `IRegistryService`.

---

## BUG-006: Incomplete Service Startup Restoration on Undo
- **Component**: `Tweakora.Core` (`ServicesActionHandler.cs`, `TweakDefinition.cs`)
- **Finding**: In WinUtil, services specify `StartupType` on apply and `OriginalType` on undo. When a tweak was undone without a prior session journal, the engine did not know the original service startup type.
- **Root Cause**: `ServiceAction` only stored the target apply startup type without capturing the upstream `OriginalType` metadata.
- **Fix**: Updated `ServiceAction` schema to store `OriginalType` (`Automatic`, `Manual`, `Disabled`), enabling exact restoration even during stateless undos.

---

## BUG-007: WinGet Error Handling and Per-User Scope Silent Skips
- **Component**: `Tweakora.Platform` (`WindowsPackageManagerService.cs`)
- **Finding**: WinGet fails with code `-1978335107` (`APPINSTALLER_CLI_ERROR_ADMIN_CONTEXT_ACTION_PROHIBITED`) when an elevated administrator tries to install or update an application installed in per-user scope. The installer reported this as an unknown failure.
- **Root Cause**: Absence of WinGet exit code mapping matching WinUtil's `Install-WinUtilProgramWinget.ps1`.
- **Fix**: Implemented complete exit code classification:
  - `0`: Success.
  - `3010`, `1641`, `-1978334967`, `-1978334965`: Success (restart required/initiated).
  - `-1978335135`: Skipped (already installed).
  - `-1978335189`: Skipped (no applicable update).
  - `-1978335107`: Skipped with explanation that elevated context cannot modify per-user package.

---

## BUG-008: Updates Tab "Disable All" Lacked Mandatory Safety Confirmation
- **Component**: `Tweakora.App` (`UpdatesViewModel.cs`, `Views/UpdatesPage.xaml`)
- **Finding**: "Disable All Updates" is a high-risk operation that disables critical security patches and background services. In previous builds, it could be triggered with a single accidental click.
- **Root Cause**: Missing interactive confirmation dialog with type-to-confirm barrier.
- **Fix**: Added high-risk modal barrier requiring the user to type `"I understand"` before the disable updates operation can be staged or executed.

---

## BUG-009: PowerShell Runner Deadlocks, Escaping Bugs, and Catalog Script Failures
- **Component**: `Tweakora.Platform` (`WindowsShellService.cs`), `Tweakora.Core` (`PowerShellActionHandler.cs`), `Catalog/Tweaks`
- **Finding**:
  1. `ExecutePowerShellAsync` executed scripts via `-Command` with manual quote replacement, causing syntax failures on complex multiline scripts.
  2. StandardOutput and StandardError were read sequentially, causing potential OS buffer deadlocks on large script outputs.
  3. On timeout, the process was killed without child tree traversal (`process.Kill()`), leaving orphaned child processes behind.
  4. Failure detection required both non-empty stderr and non-zero exit code, masking silent script failures.
  5. Several catalog scripts contained nonexistent external cmdlets (`Invoke-WinUtilExplorerUpdate`), nonexistent services (`wermgr`), invalid PowerShell parameters on native EXEs (`DISM ... -ErrorAction SilentlyContinue`), and unrealistic timeouts (60s for DISM cleanup, Edge uninstall, OneDrive uninstall).
- **Root Cause**: Lack of structured process execution harnessing, un-encoded script argument passing, sequential stream I/O, and unvalidated upstream script migration.
- **Fix**:
  1. Rewrote `ExecutePowerShellAsync` to pass scripts via `-EncodedCommand` (UTF-16LE Base64) wrapped in `$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'` and try/catch.
  2. Implemented concurrent asynchronous stream reading with `Stopwatch` duration tracking and full process tree termination (`Kill(entireProcessTree: true)`) on cancellation.
  3. Created `PowerShellResult` model with explicit `ExitCode`, `StdOut`, `StdErr`, `TimedOut`, `Duration`, and `Success` fields.
  4. Replaced `Invoke-WinUtilExplorerUpdate` with standard `Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue`.
  5. Sanitized native CLI invocations (DISM, cleanmgr), guarded service calls (`diagtrack`, `WerSvc`), raised timeouts (300s/600s), and added AST syntax parse testing in `CatalogPowerShellSyntaxTests.cs`.

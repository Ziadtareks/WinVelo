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

---

## BUG-010: Dishonest Result Reporting on Batch Operations in UI and CLI
- **Component**: `Tweakora.App` (`DashboardViewModel`, `TweaksViewModel`, `ConfigViewModel`), `Tweakora.Cli` (`Program.cs`), `Tweakora.Core` (`OperationRunner`, `TweakRunner`)
- **Finding**:
  1. In `DashboardViewModel`, applying profiles (Everyday, Gaming, Privacy, Restore Defaults) unconditionally reported success ("Profile applied!" / "Restore Defaults applied!") and returned success colors even when tweaks failed or the process ran without administrative rights.
  2. In `TweaksViewModel`, batch execution set cards to `IsActive = true` indiscriminately across all selected items without verifying which specific tweaks succeeded, and reported "Applied all tweaks successfully." on partial failures.
  3. In `ConfigViewModel`, features and fixes reported unconditional success without reflecting failure counts or details.
  4. In `Tweakora.Cli`, running `--tweaks`, `--config`, or `--preset` with partial failures printed success logs, returned exit code 0, and omitted structured failure information.
- **Root Cause**: Batch operation handlers relied on monolithic execution flags rather than itemized result verification, lacking failure aggregation and granular status mapping.
- **Fix**:
  1. Updated `OperationRunner` and `TweakRunner` with `Applied`, `Restored`, `Failed`, `FailedRolledBack`, and `FailedRollbackIncomplete` statuses, explicit `IsFailure` checks, and `TotalRequested` metrics.
  2. Enforced strict honest string formatting: `"Applied {succ} of {total} — {fail} failed (details)"` and `"Restored {succ} of {total} — {fail} failed (details)"`.
  3. Populated detailed execution failure dialogs in `DashboardTabView`, `TweaksTabView`, and `ConfigTabView` with error reasons.
  4. In `TweaksViewModel`, guaranteed that only succeeded items have `IsActive = true`; failed items retain their pre-operation state.
  5. In `Tweakora.Cli`, printed `[Failed] <id>: <reason>` for each failure, formatted `"Applied {succ} of {total} — {fail} failed"`, and exited with non-zero exit code (`1`) on any failure.

---

## BUG-011: Flawed Undo Engine, Journal Replay Corruption, and Missing Script Undo Execution
- **Component**: `Tweakora.Core` (`TweakRunner.cs`, `RollbackJournalManager.cs`, `JournalActionRecord.cs`, `IActionHandler.cs`)
- **Finding**:
  1. `UndoTweakAsync` relied solely on journal records whenever any existed. PowerShell/Script actions have `PreviousData = null`, so their journal undo was a no-op while catalog `undo` scripts were ignored (e.g. undoing Edge removal never ran the reinstall script, yet reported "Restored").
  2. Stateless undos did not isolate script actions from registry actions; applying catalog undo registry actions risked overwriting genuine user pre-tweak registry baselines with hardcoded catalog defaults.
  3. Journal records were never marked reverted after an undo; re-applying a tweak after external modifications retained the stale baseline, corrupting subsequent rollbacks.
  4. Partial failure rollback did not distinguish between complete rollback (`FailedRolledBack`) and partial rollback failure (`FailedRollbackIncomplete`), and left orphaned baseline records active in the journal.
- **Root Cause**: The rollback engine treated all actions uniformly without separating stateful baseline restoration (Registry, Service, Task, File) from stateless compensations (PowerShell, Script), and lacked lifecycle reversion tracking on journal records.
- **Fix**:
  1. Refactored `UndoTweakAsync` to restore stateful actions strictly from active journal baselines in reverse order, while executing matching catalog `undo` scripts for stateless actions without overwriting restored registry baselines.
  2. Added `IsReverted` and `RevertedAtUtc` fields to `JournalActionRecord`, and added `MarkTweakRecordsReverted` / `MarkRecordsReverted` to `RollbackJournalManager`. Updated baseline discovery to ignore reverted records so subsequent applies capture fresh baselines.
  3. Added structured step aggregation in `ActionResult` (`ExecutedSteps`, `FailedSteps`) and gated success strictly on zero failed steps.
  4. Updated partial failure rollback in `ApplyBatchAsync` to mark rolled back records as reverted and report `Failed (rolled back)` vs `Failed (rollback incomplete)`.

---

## BUG-012: Irreversible Destructive Tweaks Allowed Without Confirmation or Warning Gates
- **Component**: `Catalog` (`tweak.schema.json`, `advanced.json`, `essential.json`), `Tweakora.Core` (`TweakDefinition.cs`, `IrreversibleItemModel.cs`), `Tweakora.Cli` (`Program.cs`), `Tweakora.App` (`TweaksViewModel.cs`, `TweaksTabView.xaml`, `DashboardViewModel.cs`, `DashboardTabView.xaml`), `CatalogValidator`
- **Finding**:
  1. Permanent destructive tweaks that delete files (purging temp directories, deleting user Edge/OneDrive profiles) or permanently purge Windows update baselines (DISM `/ResetBase`) could be applied silently and indiscriminately without any warning or confirmation.
  2. The catalog schema and data models had no property to distinguish reversible tweaks from irreversible operations.
  3. The CLI allowed applying irreversible tweaks without explicit confirmation flags.
  4. The UI allowed 1-click execution of irreversible tweaks without presenting what cannot be restored.
- **Root Cause**: The catalog schema and execution engines lacked an `irreversible` safety attribute and confirmation gates.
- **Fix**:
  1. Added `reversible` (boolean, default `true`) and `irreversibleReason` (string, minLength 5) to `Catalog/schema/tweak.schema.json` with an `allOf` validation enforcing non-empty `irreversibleReason` when `reversible: false`.
  2. Added matching properties to `TweakDefinition.cs` and enforced validation in `CatalogValidator`.
  3. Audited catalog and marked 6 permanent operations as `reversible: false` with detailed warnings and reasons: `advanced.removeedge`, `advanced.removeonedrive`, `advanced.windowsai`, `essential.widget`, `essential.diskcleanup`, and `essential.deletetempfiles`.
  4. Added CLI gate `CheckIrreversibleTweaksGate` in `Program.cs` that refuses to apply irreversible tweaks without `--yes-irreversible`, displaying the affected tweaks and reasons.
  5. Implemented unmissable confirmation dialog modals in `TweaksTabView` and `DashboardTabView` backed by `RequestIrreversibleConfirmationAsync` in `TweaksViewModel` and `DashboardViewModel`, allowing users to review permanent modifications and confirm or cancel before execution.

---

## BUG-013: Single-Action State Detection Masked Composite Tweaks and Produced False Positives
- **Component**: `Tweakora.Core` (`StateDetector.cs`, `TweakState.cs`), `Tweakora.Modules` (`TweakService.cs`, `ITweakService.cs`), `Tweakora.Cli` (`Program.cs`), `Tweakora.App` (`TweakCardModel`, `TweaksViewModel.cs`, `TweaksTabView.xaml`), `Tweakora.Tests` (`CompositeStateDetectionTests.cs`)
- **Finding**:
  1. State detection previously evaluated only a single registry key or service rule from `tweak.Detect` (e.g. `essential.services` changes 30 services but checked only `CscService`). If only 1 service was disabled while 29 were left running, the app reported the tweak as 100% "Applied".
  2. Tweaks with multiple registry/policy modifications had no intermediate state representation; partial configurations either caused false positives (if checked via a single sentinel) or false negatives.
  3. Services did not verify configured startup types against declared states.
  4. Pure PowerShell tweaks with no inspectable system state guessed state or defaulted to false without indicating inability to verify.
  5. The UI only supported binary on/off toggles without representing partial or unverifiable states.
- **Root Cause**: `StateDetector` relied strictly on single-rule `tweak.Detect` rather than deriving composite multi-action verification from `tweak.Apply`, and lacked a multi-state enum (`TweakState`) to differentiate between `NotApplied`, `Applied`, `Partial`, and `Unknown`.
- **Fix**:
  1. Created `TweakState` enum (`NotApplied = 0`, `Applied = 1`, `Partial = 2`, `Unknown = 3`).
  2. Rewrote `StateDetector.DetectTweakStateAsync` to derive detection directly from `tweak.Apply` stateful actions: verifies all Registry values, policies, service startup types, and scheduled tasks. If all match -> `Applied`; if some match -> `Partial`; if none match -> `NotApplied`. Explicit `detect.any` blocks are evaluated for alternative policies (e.g. GPO vs user settings for Bing search and Telemetry).
  3. Returns `Unknown` for pure PowerShell or uninspectable tweaks rather than guessing.
  4. Updated `TweakCardModel` with nullable `bool? ToggleCheckedState` (tri-state indeterminate for `Partial`), `State` property, and distinct visual badges (`PARTIAL ⚠` in amber/yellow outline, `CAN'T VERIFY` in subtle gray, `APPLIED ✓` in green).
  5. Updated CLI `winvelo-cli --detect-all` to count and report `Applied`, `Partial`, `Default`, and `Unknown` states across the catalog.
  6. Added comprehensive unit tests in `CompositeStateDetectionTests.cs`.

---

## BUG-014: Fallback Undo Created Corrupt Journal Baseline and Left Unfinished Sessions
- **Component**: `Tweakora.Core` (`TweakRunner.cs`, `RollbackJournalManager.cs`, `IActionHandler.cs`, `RegistryActionHandler.cs`, `ServiceActionHandler.cs`, `TaskActionHandler.cs`, `PowerShellActionHandler.cs`)
- **Finding**:
  1. When no active journal records existed for a tweak, the fallback undo executed catalog actions through `handler.ExecuteAsync`. Because `handler.ExecuteAsync` unconditionally performed write-ahead journaling, it captured the tweak's APPLIED value as a new baseline record and left an uncompleted journal session on disk.
  2. Because those newly created fallback records were never marked reverted, a subsequent `apply` found the wrong baseline via `FindExistingBaselineRecord`, corrupting all future rollbacks (restoring the applied value instead of default).
  3. When an undo failed during a later step (e.g. PowerShell failure), already restored stateful records were not marked reverted, causing retries to replay stale records.
  4. Undo operations could leave open/unfinished journal sessions.
- **Root Cause**: Action handlers lacked an execution context flag to suppress write-ahead journaling during undo operations, and `UndoTweakAsync` did not mark restored stateful records reverted upon successful restoration nor ensure session completion.
- **Fix**:
  1. Added `IsUndoOperation` flag to `ExecutionContext`. Handlers (`RegistryActionHandler`, `ServiceActionHandler`, `TaskActionHandler`, `PowerShellActionHandler`) skip `RecordActionWriteAhead` and return null `JournalRecord` when `IsUndoOperation` is true.
  2. Wrapped `UndoTweakAsync` execution with `IsUndoOperation = true` in a try/finally block to guarantee no baseline records are created during any undo.
  3. In stateful undo restoration, immediately marked each restored record reverted via `_context.Journal.MarkRecordsReverted(new[] { record })` so that retrying an undo after a subsequent step failure never replays stale records.
  4. Added `HasActiveSession` property to `RollbackJournalManager` and ensured any active session is safely completed at the end of `UndoTweakAsync`.
  5. Added regression tests in `UndoEngineTests.cs` for pre-applied fallback lifecycle, retry without replaying stale records, and session completion cleanliness.

---

## BUG-015: Detection detect.any Short-Circuited Stateful Verification of Composite Tweaks
- **Component**: `Tweakora.Core` (`StateDetector.cs`), `Tweakora.Tests` (`RealRegistrySyncTests.cs`, `CompositeStateDetectionTests.cs`), `docs/TWEAK_GUIDE.md`
- **Finding**:
  1. `StateDetector.EvaluateTweakState` checked `tweak.Detect?.Any` first and returned `Applied` as soon as any single rule matched (`tweak.Detect.Any.Any(EvaluateRule)`).
  2. For composite tweaks like `essential.telemetry` (which modifies 12 distinct registry values and services), having just one policy key set caused the engine to report `Applied`, completely bypassing verification of the other 11 apply actions.
  3. `DetectTweakStateAsync` catch block failed safe to `NotApplied` instead of `Unknown` when an unhandled exception occurred during detection.
- **Root Cause**: `StateDetector` prioritized top-level `detect.any` rules over verifiable `apply` actions, conflating alternative keys for a single setting with multi-setting composite tweaks.
- **Fix**:
  1. Refactored `StateDetector.EvaluateTweakState`: if a tweak has verifiable `apply` actions, the engine always evaluates them.
  2. For single-setting tweaks where 100% of verifiable actions are declared mutually exclusive alternatives in `detect.any` (e.g. `pref.bingsearch` with `DisableSearchBoxSuggestions` vs `BingSearchEnabled`), matching any alternative reports `Applied`.
  3. For composite tweaks (e.g. `essential.telemetry`), each verifiable action must be satisfied (either directly or via an alternative rule in `detect.any` targeting the same setting/valueName, such as GPO vs system policy for `AllowTelemetry`). If only some actions match, it reports `Partial`; if all match, it reports `Applied`.
  4. Updated `DetectTweakStateAsync` catch block to fail safe to `Unknown`.
  5. Updated `RealRegistrySyncTests.cs` (`DisableTelemetry_ExternalRegistrySync_DetectsChangeImmediately`) to assert `Partial` when only 1 policy key is set and `Applied` when all keys are set.
  6. Added regression tests in `CompositeStateDetectionTests.cs` and documented the rule in `docs/TWEAK_GUIDE.md`.

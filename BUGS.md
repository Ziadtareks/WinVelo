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

---

## BUG-016: Native Command Failures Invisible in PowerShell Scripts
- **Component**: `Catalog/Tweaks/*.json` (`advanced.json`, `essential.json`), `Tweakora.Tests` (`CatalogPowerShellSyntaxTests.cs`)
- **Finding**: In Windows PowerShell 5.1, executing external native CLI tools (e.g., `winget`, `dism`, `powercfg`, `cleanmgr`, `netsh`) does not throw a terminating or non-terminating PowerShell exception when the process exits with a non-zero error code. Because the PowerShell runner wrapped script execution in try/catch, scripts that suffered complete native command failures continued running silently and returned success with exit code 0.
- **Root Cause**: PowerShell 5.1 treats native executable stderr output and non-zero exit codes as normal pipeline output rather than exceptions, requiring explicit post-call checks against `$LASTEXITCODE`.
- **Fix**:
  1. Audited all PowerShell actions calling native CLI utilities across the catalog.
  2. Added explicit `$LASTEXITCODE` checks after all native tool invocations:
     - `advanced.removeedge`: checked `$proc.ExitCode` on `setup.exe` uninstaller and `$LASTEXITCODE` on `winget install`.
     - `advanced.removeonedrive`: checked `$proc.ExitCode` on `OneDriveSetup.exe` uninstaller and `$LASTEXITCODE` on `winget install`.
     - `advanced.reservedstorage`: checked `$LASTEXITCODE` on `DISM` apply and undo (allowing reboot-required code 3010).
     - `advanced.windowsai`: checked `$LASTEXITCODE` on `winget uninstall` (allowing known-OK codes 3010, -1978335135, -1978335189, -1978335212).
     - `advanced.teredo`: checked `$LASTEXITCODE` on `netsh` apply and undo.
     - `essential.hiber`: checked `$LASTEXITCODE` on `powercfg.exe` apply and undo.
     - `essential.diskcleanup`: checked `$LASTEXITCODE` on `cleanmgr.exe` and `Dism.exe`.
  3. Added an automated regression test `AllCatalogPowerShellScripts_NativeCommandsMustCheckLastExitCode` in `CatalogPowerShellSyntaxTests.cs` that parses all catalog scripts and fails if any native command invocation (`winget`, `dism`, `powercfg`, `fsutil`, `bcdedit`, `reg`, `sc`, `cleanmgr`, `cipher`, `compact`, `vssadmin`, `wevtutil`, `netsh`) is not immediately followed by a `$LASTEXITCODE` validation.

---

## BUG-017: Aggressive Explorer Kill Left Blank Screen When Explorer Failed to Auto-Restart
- **Component**: `Catalog/Tweaks/preferences.json` (`pref.hiddenfiles`, `pref.startmenurecommendations`, `pref.taskbaralignment`)
- **Finding**: Several catalog tweaks invoked raw `Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue`. In many Windows environments (or under heavy load / policy configurations), Windows Explorer does not restart automatically upon termination, leaving the user with a frozen or completely blank desktop with no taskbar. Furthermore, tweaks like `pref.hiddenfiles` did not require killing Explorer at all because `WindowsSettingNotifier` already broadcasts `SHCNE_ASSOCCHANGED` and `WM_SETTINGCHANGE`.
- **Root Cause**: Scripts assumed Explorer would always auto-spawn and failed to poll or restart `explorer.exe` if it did not relaunch, while other tweaks needlessly killed Explorer when shell notifications were sufficient.
- **Fix**:
  1. For `pref.hiddenfiles`, removed the PowerShell Explorer termination script entirely from apply and undo, relying on `WindowsSettingNotifier`'s non-blocking `SHChangeNotify(SHCNE_ASSOCCHANGED, ...)` broadcast to refresh File Explorer views.
  2. For tweaks that require an Explorer restart (`pref.startmenurecommendations`, `pref.taskbaralignment`), replaced the raw kill with a safe restart snippet that polls for Explorer's return up to 5 seconds and explicitly launches `explorer.exe` if not detected:
     `Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; $timeout = 5; while ($timeout -gt 0 -and !(Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 500; $timeout -= 0.5 }; if (!(Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }`
  3. Verified that all tweaks requiring Explorer reload are flagged with `requiresExplorerRestart: true` in the catalog.

---

## BUG-018: Write-Ahead Journal Unfinished Sessions Orphaned Without Crash Recovery
- **Component**: `Tweakora.Core` (`RollbackJournalManager.cs`, `TweakRunner.cs`), `Tweakora.Modules` (`ITweakService.cs`, `TweakService.cs`), `Tweakora.Cli` (`Program.cs`), `Tweakora.App` (`MainWindow.xaml`, `MainWindow.xaml.cs`, `MainViewModel.cs`), `Tweakora.Tests` (`JournalCrashRecoveryTests.cs`)
- **Finding**:
  1. `RollbackJournalManager.GetUnfinishedSessions` successfully identified interrupted/uncompleted journal sessions on disk, but neither CLI nor GUI consumed them.
  2. If the application or system crashed mid-apply, partial state modifications remained unmanaged, and subsequent runs ignored the unfinished session files.
  3. Users had no automated way to rollback partial changes or mark uncompleted sessions resolved.
- **Root Cause**: The application lacked startup crash recovery detection in both CLI and GUI lifecycle handlers, and the engine lacked batch session rollback orchestration.
- **Fix**:
  1. Added `CompleteSession(RollbackJournalSession session)` and `CompleteSession(string sessionId)` to `RollbackJournalManager` to persist completion state for arbitrary sessions on disk.
  2. Implemented `RollbackSessionAsync(RollbackJournalSession session)`, `RollbackAllUnfinishedSessionsAsync()`, and `KeepAndCompleteAllUnfinishedSessions()` on `TweakRunner` (and forwarded via `ITweakService`). Replays records in reverse order, restores original baseline values, marks records reverted, and marks the session complete.
  3. Added CLI startup check in `Program.cs` that warns of unfinished sessions, displays affected tweaks and action counts, prompts `Run rollback recovery? (y/n)`, and accepts `--recover rollback` / `--recover keep` flags.
  4. Added GUI startup recovery dialog overlay in `MainWindow.xaml` and `MainViewModel.cs` showing: "A previous session did not finish. X actions were applied to [tweak names]." with "Roll back these changes" and "Keep and mark complete" buttons.
  5. Added comprehensive regression tests in `JournalCrashRecoveryTests.cs` verifying baseline value restoration, keep without revert, and GUI dialog execution.

---

## BUG-019: Hardcoded Profile Filters, Blind Auto-Apply, and Dishonest Rollback Claims
- **Component**: `Tweakora.App` (`DashboardViewModel.cs`, `DashboardTabView.xaml`, `ProfilePreviewItemModel.cs`), `Tweakora.Cli` (`Program.cs`), `Catalog/Presets`, `tools/CatalogValidator`
- **Finding**:
  1. `DashboardViewModel` used hardcoded string-matching queries (`Origin` matches, `Description.Contains("Game")`, `Take(14)` / `Take(18)`, and a dead `Category=="Essential"` filter) instead of declarative preset definitions from disk.
  2. Clicking a profile button applied tweaks directly to the host without displaying a preview of what was about to be changed, their risk levels, reversibility, or whether they were already applied.
  3. "Restore Windows Defaults" only checked 8 arbitrary quick-toggles in memory, dishonestly claiming to revert Windows defaults while completely ignoring all applied catalog tweaks and third-party tool modifications.
  4. `SmartAnalyzeAndOptimize` executed modifications immediately upon clicking rather than recommending tweaks for user confirmation.
- **Root Cause**: UI prototyping shortcuts conflating preset definitions with procedural LINQ filters and lacking an interactive staging and review dialog.
- **Fix**:
  1. Replaced hardcoded queries with declarative preset definitions in `Catalog/Presets/*.json` with explicit tweak IDs.
  2. Added `ProfilePreviewDialog` displaying tweak name, risk, reversibility, reboot/explorer restart requirements, and current state. Already-applied tweaks are skipped automatically and shown as such.
  3. Renamed profile descriptions honestly (e.g. "Gaming: Fewer Background Interruptions" without unmeasurable claims like "Max FPS Boost").
  4. Replaced "Restore Windows Defaults" with "Undo all WinVelo changes", which queries active write-ahead journal records (newest first), shows the same preview dialog, and provides a clear disclaimer that changes made by third-party tools or Windows updates are not covered.
  5. Updated `SmartAnalyzeAndOptimize` to only recommend tweaks and open the preview dialog without auto-applying.
  6. Enforced in `CatalogValidator` and unit tests that every preset references existing IDs only, contains zero High-risk tweaks, zero irreversible tweaks, and zero tweaks marked `needsReview`.

---

## BUG-020: Windows Servicing Components Left Unprotected Against Destructive Tweaks and Lacking Health Integrity Verification
- **Component**: `Tweakora.Core` (`ServicingProtection.cs`), `tools/CatalogValidator` (`Program.cs`), `Catalog/Tweaks` (`essential.json`), `Catalog/schema` (`tweak.schema.json`), `Tweakora.Modules` (`TweakService.cs`), `Tweakora.Cli` (`Program.cs`), `Tweakora.App` (`DashboardViewModel.cs`), `Tweakora.Tests` (`ServicingProtectionTests.cs`)
- **Finding**:
  1. The catalog validator allowed tweaks to disable or alter critical Windows servicing components (such as Windows Update, BITS, Store InstallService, TrustedInstaller, Defender, etc.) without safety gating.
  2. `essential.disablestoresearch` denied permissions to the Store database (`store.db`) using `icacls ... /deny *S-1-1-0:F`, which broke Microsoft Store operations and app updates, yet was categorized as "Safe" without any side-effects disclosure.
  3. No post-apply servicing verification existed in either GUI or CLI to ensure system servicing remained operational after applying tweaks or presets.
  4. The CLI had no command to inspect or repair servicing component health.
- **Root Cause**: Absence of a protected servicing inventory in the catalog schema and engine safety layer, allowing servicing-destructive tweaks to pass validation without health checks.
- **Fix**:
  1. Defined `ServicingProtectionConstants` listing the 15 critical protected services (`wuauserv`, `BITS`, `UsoSvc`, `WaaSMedicSvc`, `DoSvc`, `TrustedInstaller`, `CryptSvc`, `AppXSvc`, `ClipSVC`, `LicenseManager`, `InstallService`, `WinDefend`, `SecurityHealthService`, `wscsvc`, `mpssvc`) and protected Windows Update / Store blocking policies.
  2. Updated `CatalogValidator` to strictly enforce that any tweak modifying protected services or policies must be classified as risk `High`, must contain a non-empty `sideEffects` explanation, and is strictly barred from all presets.
  3. Re-evaluated `essential.disablestoresearch`, elevating its risk to `High` and documenting its side effects on Microsoft Store package management.
  4. Added `ServicingProtectionHelper.CheckServicingHealthAsync` and `RestoreServicingComponentsAsync` to check and repair servicing health.
  5. Integrated post-apply servicing health check into `DashboardViewModel` and CLI `ExecutePresetAsync`.
  6. Added CLI command `--health-check [--restore]`.
---

## BUG-021: Placebo Tweaks, Unproven Performance Claims, and Optimization Score Gaming
- **Component**: `Catalog/Tweaks`, `Catalog/schema`, `tools/CatalogValidator`, `Tweakora.App` (`DashboardViewModel`, `DashboardTabView`, `TweaksViewModel`, `TweaksTabView`, `StartupViewModel`, `StartupTabView`, `ToolboxViewModel`, `ToolboxPage`, `MainViewModel`), `Tweakora.Modules` (`NetworkOptimizerService`, `MemoryOptimizerService`), `Tweakora.Tests` (`PlaceboAndClaimScrubTests.cs`)
- **Finding**:
  1. The catalog lacked an evidence classification system, allowing unproven community claims (such as reducing `MouseDataQueueSize` or CPU unparking on modern Windows schedulers) to be presented alongside documented tweaks.
  2. Tweaks addressing situational hardware bugs (such as disabling MPO to prevent multi-monitor flicker or disabling power throttling on plugged-in machines) were marketed as universal performance/FPS boosts without disclosing battery life or windowed presentation trade-offs.
  3. Genuine user preferences (mouse acceleration curve disabling, HAGS) were miscategorized as system performance enhancements.
  4. The UI featured an "Optimization Score" that rewarded users for blindly applying more tweaks, incentivizing over-tweaking and damaging system stability.
  5. The UI featured unmeasured claims ("Easy Boost", "faster boot", "saves ~1.2s on boot") that could not be empirically validated.
  6. Global adapter-level Nagle algorithm disabling and standby list RAM purging were promoted without disclaimers about gaming socket/UDP reality or Windows memory caching mechanics.
- **Root Cause**: Absence of an evidence hierarchy in the schema and adoption of traditional optimizer marketing tropes instead of truthful, evidence-based descriptions.
- **Fix**:
  1. Added schema property `evidence` (`"documented"`, `"situational"`, `"anecdotal"`) and `Experimental` / `preferences` categories.
  2. Moved anecdotal items (`performance.input.buffer-queue`, `performance.cpu.unpark-cores`) to `Experimental` category, excluded from presets, and hidden behind a user toggle "Show experimental tweaks" (off by default) with clear notices.
  3. Relabeled situational tweaks (MPO, power throttling, priority separation) with honest bug-fix or trade-off descriptions rather than performance claims.
  4. Reclassified user preferences (mouse acceleration, HAGS) under `category: "preferences"`.
  5. Enforced in `CatalogValidator` that presets contain ONLY `evidence: "documented"` tweaks and zero `Experimental` tweaks.
  6. Completely replaced the "Optimization Score" banner with an honest "Configured System Settings" meter (`{appliedCount} of {totalCount} settings configured`).
  7. Scrubbed unmeasured claims ("Easy Boost", "faster boot", boot-time-saved numbers) from the UI and Startup manager, labeling startup impact as "Estimated (heuristic)".
  8. Documented the real mechanics and limitations of Nagle disabling and standby RAM purging in `NetworkOptimizerService`, `MemoryOptimizerService`, `ToolboxViewModel`, and `ToolboxPage`.
  9. Added comprehensive unit tests in `PlaceboAndClaimScrubTests.cs`.

---

## BUG-022: Uncalibrated Risk Ratings and Absence of Minimum Risk Floors
- **Component**: `Catalog/Tweaks`, `tools/CatalogValidator`, `docs/TWEAK_GUIDE.md`, `docs/RISK_AUDIT.md`, `src/Tweakora.Tests/RiskRubricTests.cs`
- **Finding**:
  1. 116 of 149 tweaks were previously rated as "Safe", despite 77 tweaks modifying machine-wide system policies in `HKLM`, disabling relied-upon features (hibernation, widgets, Copilot, background apps), altering services, or requiring a system reboot.
  2. Irreversible tweaks and machine-level security and networking changes had arbitrary risk ratings with no formal criteria or minimum floors.
  3. No written risk rubric existed to govern tweak classification or authoring guidelines.
- **Root Cause**: Absence of a formalized, objective risk rubric with validator-enforced minimum floors.
- **Fix**:
  1. Formulated a written 4-tier risk rubric in `docs/TWEAK_GUIDE.md` (Safe = per-user HKCU UI preference, instantly reversible, no feature lost; Low = machine policy HKLM, reversible, no feature a typical user relies on is lost; Medium = turns off a Windows feature some users rely on or requires reboot; High = services, security, updates, Store, networking stack, or irreversible).
  2. Re-scored all 149 tweaks against the rubric (Safe: 45, Low: 19, Medium: 48, High: 37).
  3. Documented every risk change in `docs/RISK_AUDIT.md` (96 tweaks recalibrated with specific one-line rationale).
  4. Enforced minimum risk floors in `CatalogValidator`: irreversible $\implies$ High; protected servicing $\implies$ High; HKLM $\implies$ at least Low (cannot be Safe).
  5. Added unit test suite in `RiskRubricTests.cs`.

---

## BUG-023: Missing Tweak Explanations, Side Effects Disclosures, Source Citations, and Recommended-Only View Filter
- **Component**: `Catalog/Tweaks`, `Catalog/schema/tweak.schema.json`, `tools/CatalogValidator/Program.cs`, `src/Tweakora.App/ViewModels/TweaksViewModel.cs`, `src/Tweakora.App/Views/TweaksTabView.xaml`, `src/Tweakora.Tests/TweakExplanationTests.cs`
- **Finding**:
  1. Tweaks lacked granular explanations of their exact system behavior (`whatItDoes`), side effects/potential trade-offs (`sideEffects`), and authoritative documentation citations (`sources`).
  2. Users had no O&O ShutUp10++-style expandable drawer on tweak cards to inspect what a tweak does, what it might break, or view documentation before toggling.
  3. No "Recommended only" view filter existed to isolate conservative, well-tested, documented tweaks (Safe & Low risk, fully reversible, non-needsReview) from aggressive or advanced settings.
  4. Non-Safe tweaks previously had no schema enforcement requiring explicit side-effects disclosure (allowing "None known" even on machine-altering tweaks).
  5. Tweaks with unverified or conflicting community claims (`performance.input.buffer-queue`, `performance.cpu.unpark-cores`, `essential.wpbt`, `pref.standbyfix`, `pref.s3sleep`) lacked a structured `needsReview: true` flag to quarantine them from presets and warn users.
- **Root Cause**: Omission of explanatory metadata fields and absence of validator enforcement requiring citations and non-trivial side effect disclosures for non-Safe tweaks.
- **Fix**:
  1. Added schema properties `whatItDoes`, `sideEffects`, `sources`, and `needsReview` to `Catalog/schema/tweak.schema.json` and `TweakDefinition.cs`.
  2. Populated all 149 tweaks with accurate, non-speculative explanations, explicit side effects (strictly forbidding "None known" on non-Safe tweaks), and verified Microsoft Learn / upstream documentation source URLs.
  3. Flagged 5 uncertain tweaks as `needsReview: true` (`performance.input.buffer-queue`, `performance.cpu.unpark-cores`, `essential.wpbt`, `pref.standbyfix`, `pref.s3sleep`), ensuring exclusion from presets and displaying warning banners in the UI.
  4. Added `ValidateExplanations` in `CatalogValidator` enforcing that all non-Safe tweaks provide `whatItDoes`, `sources` ($\ge 1$ valid HTTP URLs), and non-empty `sideEffects` that cannot be "None known".
  5. Implemented expandable info drawer on tweak cards in `TweaksTabView.xaml` and `TweakCardModel` with "Details ▾" toggle, showing "WHAT IT DOES", "SIDE EFFECTS / POTENTIAL IMPACT", `NeedsReview` warning alert, and clickable documentation source links.
  6. Added "Recommended only" view filter to `TweaksViewModel` and `TweaksTabView.xaml` allowing users to focus exclusively on conservative, well-documented, reversible settings.
  7. Added unit test suite in `TweakExplanationTests.cs` validating explanations, source URLs, side effects rubric, `needsReview` preset quarantine, and the recommended-only filter.

---

## BUG-024: CLI Modifying Commands Lacked Interactive Confirmation and Non-Interactive Safety Guard
- **Component**: `src/Tweakora.Cli/Program.cs`, `src/Tweakora.Tests/CliConfirmationTests.cs`
- **Finding**:
  1. CLI commands that modify host machine state (`--apply`, `--undo`, `--preset ... --run`, `--config ... --run`, `--recover rollback`) could be executed without interactive confirmation or explicit authorization (`--yes`), allowing accidental execution against the host OS.
  2. In non-interactive environments (e.g. background tasks, scripts, CI, redirected standard input), commands could execute silently without an explicit `--yes` flag.
- **Root Cause**: Absence of a confirmation gate on mutating CLI execution paths.
- **Fix**:
  1. Implemented `Program.CheckConfirmationGate(args, operationDescription)` in `src/Tweakora.Cli/Program.cs`.
  2. Enforced that any command mutating host state prompts interactively for confirmation `(y/N)` unless `--yes` / `-y` is supplied.
  3. When standard input is non-interactive (`Console.IsInputRedirected == true`), commands strictly refuse to execute unless `--yes` is explicitly passed.
---

## BUG-025: Catalog Explanations Desynchronized From Actual Action Payloads and Missing Auto-Generated Technical Change Specifications
- **Component**: `Catalog/Tweaks/*.json`, `src/Tweakora.Core/Utilities/TweakActionFormatter.cs`, `src/Tweakora.App/ViewModels/TweaksViewModel.cs`, `src/Tweakora.App/Views/TweaksTabView.xaml`, `src/Tweakora.Cli/Program.cs`, `src/Tweakora.Tests/TweakExplanationTests.cs`, `src/Tweakora.Tests/CliConfirmationTests.cs`
- **Finding**:
  1. Human-written descriptions (`whatItDoes` and `sideEffects`) in several catalog tweaks contradicted actual underlying action data:
     - `performance.input.buffer-queue`: text stated `MouseDataQueueSize=16` (actions set both `MouseDataQueueSize=20` and `KeyboardDataQueueSize=20`).
     - `performance.cpu.unpark-cores`: text stated `ValueMax=100%` (apply action sets `ValueMax=0`).
     - `performance.gaming.mmcss-responsiveness`: text stated `SystemResponsiveness=0` (apply action sets `SystemResponsiveness=10`, `NetworkThrottlingIndex=-1`, `GPU Priority=8`, `Priority=6`).
     - `essential.services`: text omitted `SvcHostSplitThresholdInKB` script setting.
     - `advanced.disablewarningforunsignedrdp`: text referred to `WarnUnsignedConnection to 0` (actions set `RedirectionWarningDialogVersion=1` and `RdpLaunchConsentAccepted=1`).
     - `advanced.disablebgapps`: text referred to `LetAppsRunInBackground to 2` (actions set `GlobalUserDisabled=1`).
     - `performance.gpu.hags`: text referred to `HagsEnabled = 2` (actions set `HwSchMode = 2`).
     - `pref.multiplaneoverlay`: text claimed `OverlayTestMode to 5` (actions set `OverlayTestMode = 1` and `DisableOverlays = 1`).
     - `privacy.telemetry.diagnostic-data`: text claimed `AllowTelemetry = 0 or 1` (apply action sets `AllowTelemetry = 0`).
  2. The UI and CLI lacked an auto-generated, deterministic technical specification of the exact engine actions (`hive\key\valueName = data`, `Service → StartupType`, `Scheduled Task → State`, `PowerShell: summary`). Users had to rely solely on human-written prose which could become outdated or inaccurate.
- **Root Cause**: Manual authoring of explanation text without automated cross-validation tests between text tokens (registry value names and setting numbers) and actual JSON action payloads.
- **Fix**:
  1. Audited all 149 catalog tweaks and aligned all descriptions with actual action payloads and registry keys.
  2. Implemented `TweakActionFormatter` in `Tweakora.Core.Utilities` generating deterministic "Exact changes" lists from apply actions for Registry, Service, Task, PowerShell, AppX, Feature, and File actions.
  3. Integrated "EXACT CHANGES (ENGINE SPECIFICATION)" into the UI details drawer (`TweaksTabView.xaml` and `TweakCardModel`).
  4. Added `--info <tweakId>` command to `winvelo-cli` displaying metadata, descriptions, side effects, and exact changes specifications.
  5. Added regression test `AllTweaks_RegistryValueNamesAndNumbersInExplanations_MatchCatalogActions` in `TweakExplanationTests.cs` enforcing that every registry value name and setting number mentioned in explanations exists in that tweak's apply/undo actions.
  6. Added tests in `TweakExplanationTests.cs` and `CliConfirmationTests.cs`.

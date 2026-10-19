# WinVelo Bug Inventory & Root Cause Analysis

This document catalogs all historical, discovered, and audited bugs, crashes, stubs, fakes, and behavioral inconsistencies across WinVelo, with their root causes and verified fixes.

---

## BUG-001: Infinite Dispatcher Loop and StackOverflowException (0xC00000FD)
- **Component**: `WinVelo.App` (`Views/TweaksPage.xaml`, `App.xaml.cs`)
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
- **Component**: `WinVelo.App` (`Views/MainWindow.xaml`, `App.xaml.cs`)
- **Finding**: Event Viewer recorded `System.InvalidOperationException: Cannot apply backdrop effect if ExtendsContentIntoTitleBar is false` from `Wpf.Ui.Controls.FluentWindow.OnBackdropTypeChanged`.
- **Root Cause**: `FluentWindow` in `WPF-UI` requires title bar content extension to be enabled before applying Windows 11 Mica/Acrylic backdrops. The XAML set backdrop properties before enabling title bar extension.
- **Fix**: Configured `ExtendsContentIntoTitleBar="True"` directly in XAML on `MainWindow` and guarded runtime backdrop application behind build-number and OS-capability checks.

---

## BUG-003: Disconnected UI Command Handlers (Fake Success in Real Mode)
- **Component**: `WinVelo.App` (`TweaksViewModel.cs`, `ToolboxViewModel.cs`, `InstallViewModel.cs`)
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
- **Component**: `WinVelo.Modules`, `WinVelo.App`
- **Finding**: Domain services contained internal `if (isSimulationMode)` branches and directly instantiated `FakeRegistryService`, bypassing the real platform driver.
- **Root Cause**: Architectural conflation between simulation mode (for automated FlaUI and unit tests) and production execution.
- **Fix**:
  - Extracted all simulation classes into a dedicated, isolated project `WinVelo.Simulation`.
  - Removed all `isSimulationMode` flags and branches from production services in `WinVelo.Modules`.
  - Added architecture tests (`NetArchTest` / reflection in `ArchitectureTests.cs`) that fail the build if any ViewModel, Module, or Service references `WinVelo.Simulation` or types prefixed with `Fake*`.
  - Simulation is exclusively configured at the composition root (`App.xaml.cs` and `Program.cs`) when `--test-mode` is explicitly supplied.

---

## BUG-005: Missing WinUtil Registry `<RemoveEntry>` Deletion Semantics
- **Component**: `WinVelo.Platform` (`WindowsRegistryService.cs`, `RegistryActionHandler.cs`)
- **Finding**: WinUtil specifies `<RemoveEntry>` as a sentinel value in `config/tweaks.json` when applying or reverting tweaks that require deleting a property rather than writing a value. The engine treated this string as literal text to write into the registry.
- **Root Cause**: Lack of explicit sentinel handling for `<RemoveEntry>` in `RegistryActionHandler`.
- **Fix**: Added explicit handling in `RegistryActionHandler`: when `Value == "<RemoveEntry>"` or `ActionType == ActionType.RegistryDeleteValue`, the handler calls `DeleteValue` via `IRegistryService`.

---

## BUG-006: Incomplete Service Startup Restoration on Undo
- **Component**: `WinVelo.Core` (`ServicesActionHandler.cs`, `TweakDefinition.cs`)
- **Finding**: In WinUtil, services specify `StartupType` on apply and `OriginalType` on undo. When a tweak was undone without a prior session journal, the engine did not know the original service startup type.
- **Root Cause**: `ServiceAction` only stored the target apply startup type without capturing the upstream `OriginalType` metadata.
- **Fix**: Updated `ServiceAction` schema to store `OriginalType` (`Automatic`, `Manual`, `Disabled`), enabling exact restoration even during stateless undos.

---

## BUG-007: WinGet Error Handling and Per-User Scope Silent Skips
- **Component**: `WinVelo.Platform` (`WindowsPackageManagerService.cs`)
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
- **Component**: `WinVelo.App` (`UpdatesViewModel.cs`, `Views/UpdatesPage.xaml`)
- **Finding**: "Disable All Updates" is a high-risk operation that disables critical security patches and background services. In previous builds, it could be triggered with a single accidental click.
- **Root Cause**: Missing interactive confirmation dialog with type-to-confirm barrier.
- **Fix**: Added high-risk modal barrier requiring the user to type `"I understand"` before the disable updates operation can be staged or executed.

---

## BUG-009: PowerShell Runner Deadlocks, Escaping Bugs, and Catalog Script Failures
- **Component**: `WinVelo.Platform` (`WindowsShellService.cs`), `WinVelo.Core` (`PowerShellActionHandler.cs`), `Catalog/Tweaks`
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
- **Component**: `WinVelo.App` (`DashboardViewModel`, `TweaksViewModel`, `ConfigViewModel`), `WinVelo.Cli` (`Program.cs`), `WinVelo.Core` (`OperationRunner`, `TweakRunner`)
- **Finding**:
  1. In `DashboardViewModel`, applying profiles (Everyday, Gaming, Privacy, Restore Defaults) unconditionally reported success ("Profile applied!" / "Restore Defaults applied!") and returned success colors even when tweaks failed or the process ran without administrative rights.
  2. In `TweaksViewModel`, batch execution set cards to `IsActive = true` indiscriminately across all selected items without verifying which specific tweaks succeeded, and reported "Applied all tweaks successfully." on partial failures.
  3. In `ConfigViewModel`, features and fixes reported unconditional success without reflecting failure counts or details.
  4. In `WinVelo.Cli`, running `--tweaks`, `--config`, or `--preset` with partial failures printed success logs, returned exit code 0, and omitted structured failure information.
- **Root Cause**: Batch operation handlers relied on monolithic execution flags rather than itemized result verification, lacking failure aggregation and granular status mapping.
- **Fix**:
  1. Updated `OperationRunner` and `TweakRunner` with `Applied`, `Restored`, `Failed`, `FailedRolledBack`, and `FailedRollbackIncomplete` statuses, explicit `IsFailure` checks, and `TotalRequested` metrics.
  2. Enforced strict honest string formatting: `"Applied {succ} of {total} — {fail} failed (details)"` and `"Restored {succ} of {total} — {fail} failed (details)"`.
  3. Populated detailed execution failure dialogs in `DashboardTabView`, `TweaksTabView`, and `ConfigTabView` with error reasons.
  4. In `TweaksViewModel`, guaranteed that only succeeded items have `IsActive = true`; failed items retain their pre-operation state.
  5. In `WinVelo.Cli`, printed `[Failed] <id>: <reason>` for each failure, formatted `"Applied {succ} of {total} — {fail} failed"`, and exited with non-zero exit code (`1`) on any failure.

---

## BUG-011: Flawed Undo Engine, Journal Replay Corruption, and Missing Script Undo Execution
- **Component**: `WinVelo.Core` (`TweakRunner.cs`, `RollbackJournalManager.cs`, `JournalActionRecord.cs`, `IActionHandler.cs`)
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
- **Component**: `Catalog` (`tweak.schema.json`, `advanced.json`, `essential.json`), `WinVelo.Core` (`TweakDefinition.cs`, `IrreversibleItemModel.cs`), `WinVelo.Cli` (`Program.cs`), `WinVelo.App` (`TweaksViewModel.cs`, `TweaksTabView.xaml`, `DashboardViewModel.cs`, `DashboardTabView.xaml`), `CatalogValidator`
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
- **Component**: `WinVelo.Core` (`StateDetector.cs`, `TweakState.cs`), `WinVelo.Modules` (`TweakService.cs`, `ITweakService.cs`), `WinVelo.Cli` (`Program.cs`), `WinVelo.App` (`TweakCardModel`, `TweaksViewModel.cs`, `TweaksTabView.xaml`), `WinVelo.Tests` (`CompositeStateDetectionTests.cs`)
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
- **Component**: `WinVelo.Core` (`TweakRunner.cs`, `RollbackJournalManager.cs`, `IActionHandler.cs`, `RegistryActionHandler.cs`, `ServiceActionHandler.cs`, `TaskActionHandler.cs`, `PowerShellActionHandler.cs`)
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
- **Component**: `WinVelo.Core` (`StateDetector.cs`), `WinVelo.Tests` (`RealRegistrySyncTests.cs`, `CompositeStateDetectionTests.cs`), `docs/TWEAK_GUIDE.md`
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
- **Component**: `Catalog/Tweaks/*.json` (`advanced.json`, `essential.json`), `WinVelo.Tests` (`CatalogPowerShellSyntaxTests.cs`)
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
- **Component**: `WinVelo.Core` (`RollbackJournalManager.cs`, `TweakRunner.cs`), `WinVelo.Modules` (`ITweakService.cs`, `TweakService.cs`), `WinVelo.Cli` (`Program.cs`), `WinVelo.App` (`MainWindow.xaml`, `MainWindow.xaml.cs`, `MainViewModel.cs`), `WinVelo.Tests` (`JournalCrashRecoveryTests.cs`)
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
- **Component**: `WinVelo.App` (`DashboardViewModel.cs`, `DashboardTabView.xaml`, `ProfilePreviewItemModel.cs`), `WinVelo.Cli` (`Program.cs`), `Catalog/Presets`, `tools/CatalogValidator`
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
- **Component**: `WinVelo.Core` (`ServicingProtection.cs`), `tools/CatalogValidator` (`Program.cs`), `Catalog/Tweaks` (`essential.json`), `Catalog/schema` (`tweak.schema.json`), `WinVelo.Modules` (`TweakService.cs`), `WinVelo.Cli` (`Program.cs`), `WinVelo.App` (`DashboardViewModel.cs`), `WinVelo.Tests` (`ServicingProtectionTests.cs`)
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
- **Component**: `Catalog/Tweaks`, `Catalog/schema`, `tools/CatalogValidator`, `WinVelo.App` (`DashboardViewModel`, `DashboardTabView`, `TweaksViewModel`, `TweaksTabView`, `StartupViewModel`, `StartupTabView`, `ToolboxViewModel`, `ToolboxPage`, `MainViewModel`), `WinVelo.Modules` (`NetworkOptimizerService`, `MemoryOptimizerService`), `WinVelo.Tests` (`PlaceboAndClaimScrubTests.cs`)
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
- **Component**: `Catalog/Tweaks`, `tools/CatalogValidator`, `docs/TWEAK_GUIDE.md`, `docs/RISK_AUDIT.md`, `src/WinVelo.Tests/RiskRubricTests.cs`
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
- **Component**: `Catalog/Tweaks`, `Catalog/schema/tweak.schema.json`, `tools/CatalogValidator/Program.cs`, `src/WinVelo.App/ViewModels/TweaksViewModel.cs`, `src/WinVelo.App/Views/TweaksTabView.xaml`, `src/WinVelo.Tests/TweakExplanationTests.cs`
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
- **Component**: `src/WinVelo.Cli/Program.cs`, `src/WinVelo.Tests/CliConfirmationTests.cs`
- **Finding**:
  1. CLI commands that modify host machine state (`--apply`, `--undo`, `--preset ... --run`, `--config ... --run`, `--recover rollback`) could be executed without interactive confirmation or explicit authorization (`--yes`), allowing accidental execution against the host OS.
  2. In non-interactive environments (e.g. background tasks, scripts, CI, redirected standard input), commands could execute silently without an explicit `--yes` flag.
- **Root Cause**: Absence of a confirmation gate on mutating CLI execution paths.
- **Fix**:
  1. Implemented `Program.CheckConfirmationGate(args, operationDescription)` in `src/WinVelo.Cli/Program.cs`.
  2. Enforced that any command mutating host state prompts interactively for confirmation `(y/N)` unless `--yes` / `-y` is supplied.
  3. When standard input is non-interactive (`Console.IsInputRedirected == true`), commands strictly refuse to execute unless `--yes` is explicitly passed.
---

## BUG-025: Catalog Explanations Desynchronized From Actual Action Payloads and Missing Auto-Generated Technical Change Specifications
- **Component**: `Catalog/Tweaks/*.json`, `src/WinVelo.Core/Utilities/TweakActionFormatter.cs`, `src/WinVelo.App/ViewModels/TweaksViewModel.cs`, `src/WinVelo.App/Views/TweaksTabView.xaml`, `src/WinVelo.Cli/Program.cs`, `src/WinVelo.Tests/TweakExplanationTests.cs`, `src/WinVelo.Tests/CliConfirmationTests.cs`
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
  2. Implemented `TweakActionFormatter` in `WinVelo.Core.Utilities` generating deterministic "Exact changes" lists from apply actions for Registry, Service, Task, PowerShell, AppX, Feature, and File actions.
  3. Integrated "EXACT CHANGES (ENGINE SPECIFICATION)" into the UI details drawer (`TweaksTabView.xaml` and `TweakCardModel`).
  4. Added `--info <tweakId>` command to `winvelo-cli` displaying metadata, descriptions, side effects, and exact changes specifications.
  5. Added regression test `AllTweaks_RegistryValueNamesAndNumbersInExplanations_MatchCatalogActions` in `TweakExplanationTests.cs` enforcing that every registry value name and setting number mentioned in explanations exists in that tweak's apply/undo actions.
  6. Added tests in `TweakExplanationTests.cs` and `CliConfirmationTests.cs`.

---

## BUG-026: Lenient Evidence Classification, Unverified Documentation Claims, and Gamified Optimization Meter
- **Component**: `Catalog/Tweaks/*.json`, `Catalog/schema/tweak.schema.json`, `src/WinVelo.Core/Models/TweakDefinition.cs`, `tools/CatalogValidator/Program.cs`, `src/WinVelo.App/ViewModels/DashboardViewModel.cs`, `src/WinVelo.App/Views/DashboardTabView.xaml`, `src/WinVelo.App/ViewModels/TweaksViewModel.cs`, `src/WinVelo.App/Views/TweaksTabView.xaml`, `src/WinVelo.Cli/Program.cs`, `docs/EVIDENCE_AUDIT.md`, `src/WinVelo.Tests/TweakExplanationTests.cs`, `src/WinVelo.Tests/PlaceboAndClaimScrubTests.cs`
- **Finding**:
  1. The initial evidence pass defaulted 132 tweaks to "documented", even when the cited source was merely a general discussion of a Windows subsystem rather than vendor documentation explicitly naming the setting (registry value, policy name, or CSP property).
  2. Tweaks with "documented" evidence lacked verification proof and verbatim source quotes (`sourceQuote`).
  3. `performance.gaming.mmcss-responsiveness` was classified as "documented", but Microsoft MMCSS documentation documents `SystemResponsiveness` clamping (10-80) and does not document `NetworkThrottlingIndex=-1` or Games task scheduling overrides.
  4. The dashboard contained a gamified "Configured Settings" percentage meter, progress bar, and grade that encouraged blanket application of tweaks rather than a transparent, neutral count.
- **Root Cause**: Overly broad definition of "documented" evidence without requiring verbatim vendor quotes explicitly naming the setting, and legacy gamified progress metrics in the dashboard.
- **Fix**:
  1. Enforced strict evidence rubric: "documented" requires vendor documentation explicitly naming the registry value name or policy property, verified against live source pages, and stored verbatim in `sourceQuote`.
  2. Re-audited all 149 tweaks: 16 classified as `documented` with explicit `sourceQuote` extracts, 131 classified as `situational`, and 2 classified as `anecdotal` (`performance.cpu.unpark-cores` and `performance.input.buffer-queue`, quarantined with `needsReview: true`).
  3. Re-evaluated `performance.gaming.mmcss-responsiveness` against Microsoft MMCSS docs and moved to `category: "Experimental"`, `evidence: "situational"`, and `needsReview: true`.
  4. Generated complete audit ledger in `docs/EVIDENCE_AUDIT.md`.
  5. Replaced "Configured Settings" meter and percentage progress bar in `DashboardTabView.xaml` and `DashboardViewModel.cs` with neutral "N tweaks applied by WinVelo" status with no score, percentage, or grade.
  6. Added `sourceQuote` to `tweak.schema.json`, `TweakDefinition.cs`, `TweakCardModel`, `TweaksTabView.xaml`, and CLI `--info`.
  7. Updated `CatalogValidator` and unit tests in `TweakExplanationTests.cs` and `PlaceboAndClaimScrubTests.cs` to enforce `sourceQuote` on all documented tweaks and prevent anecdotal/experimental tweaks in presets.

---

## BUG-027: Ambiguous Evidence Semantics for Ordinary UI Settings and Incomplete Recommended-Only Criteria
- **Component**: `Catalog/Tweaks/*.json`, `Catalog/schema/tweak.schema.json`, `src/WinVelo.Core/Models/TweakDefinition.cs`, `tools/CatalogValidator/Program.cs`, `src/WinVelo.App/ViewModels/TweaksViewModel.cs`, `src/WinVelo.App/Views/TweaksTabView.xaml`, `src/WinVelo.Cli/Program.cs`, `docs/EVIDENCE_AUDIT.md`, `src/WinVelo.Tests/TweakExplanationTests.cs`, `src/WinVelo.Tests/PlaceboAndClaimScrubTests.cs`
- **Finding**:
  1. The previous audit grouped 131 tweaks under `evidence: "situational"`. Most of these were ordinary Windows Settings (e.g., dark mode, file extensions, taskbar alignment, hidden files, transparency), which broke the intended definition of "situational" as "fixes a specific named problem or workaround".
  2. Users looking at ordinary UI preferences had no indication where that setting could be toggled natively in Windows Settings or Control Panel.
  3. The "Recommended only" view filter previously checked only `documented` evidence, excluding safe and standard UI settings that have direct GUI equivalents in Windows.
- **Root Cause**: Absence of an explicit `ui-equivalent` evidence level that captures settings directly configurable via standard Windows GUI dialogs and pages.
- **Fix**:
  1. Introduced `ui-equivalent` evidence tier in `tweak.schema.json`, `TweakDefinition.cs`, `TweakCardModel`, and `CatalogValidator`.
  2. Added required `uiEquivalentLocation` field specifying exact navigation path (e.g. `Settings › Personalization › Colors`).
  3. Reclassified all 149 tweaks: 16 `documented`, 71 `ui-equivalent`, 60 `situational` (strictly fixing named problems), 2 `anecdotal` (`needsReview: true`).
  4. Updated "Recommended only" filter criteria: `(Risk is Safe or Low) && (Evidence is documented or ui-equivalent) && Reversible && !NeedsReview && (Category != "Experimental")`.
  5. Added "⚙️ WHERE TO FIND THIS IN WINDOWS:" disclosure banner to expanded cards in `TweaksTabView.xaml` and CLI `--info`.
  6. Updated `docs/EVIDENCE_AUDIT.md`, schema validator, and regression tests (`UiEquivalentTweaks_HaveNonEmptyUiLocation`, `TweaksViewModel_RecommendedOnlyFilter_ShowsOnlySafeAndLowDocumentedReversible`).

---

## BUG-028: Navigation Tab Bloat and Fragmented System Utilities Architecture
- **Component**: `src/WinVelo.App/Views/MainWindow.xaml`, `src/WinVelo.App/ViewModels/MainViewModel.cs`, `src/WinVelo.App/ViewModels/ToolsViewModel.cs`, `src/WinVelo.App/Views/ToolsTabView.xaml`, `src/WinVelo.App/ViewModels/HistoryViewModel.cs`, `src/WinVelo.App/Views/HistoryTabView.xaml`, `src/WinVelo.App/App.xaml.cs`
- **Finding**:
  1. The application shell exposed 8 disparate top-level navigation tabs (`Dashboard`, `Tweaks`, `Install`, `Config`, `Updates`, `Toolbox`, `Startup`, `Backups`), leading to cognitive clutter, duplicated entry points, and inconsistent categorization.
  2. System configuration utilities (Startup apps, Services, Cleaner, DNS, Windows Fixes, Windows Update Mode, Optional Features, and Restore Points) were fragmented across multiple tabs and side drawers.
  3. Rollback journal inspection and backup restoration were split between separate views rather than unified under a single chronological History surface.
- **Root Cause**: Uncoordinated growth of feature tabs without an integrated information hierarchy or consolidated navigation shell.
- **Fix**:
  1. Consolidated top-level navigation from 8 tabs down to 5: `Home` (Dashboard), `Tweaks`, `Apps` (Install), `Tools`, and `History`.
  2. Implemented `ToolsViewModel` and `ToolsTabView` housing 8 sub-tools with a clean pill toolbar: Startup apps, Services, System Cleaner, DNS switcher, Windows Fixes, Windows Updates mode, Optional Features, and Restore Points.
  3. Implemented `HistoryViewModel` and `HistoryTabView` merging journal tracking, session history (newest first), individual tweak rollbacks, session rollbacks, "Undo all WinVelo changes", session log `.txt` exports, and restore point indicators.
  4. Updated `MainViewModel` to support 5 primary tabs while preserving backward-compatible routing for legacy command links and updating the Ctrl+K command palette.
  5. Added DI registrations and updated architecture integrity and UI diagnostic tests.

---

## BUG-029: Gimmicky Telemetry Gauges, 1-Second Polling Timers, and Missing Calm System Status Indicators
- **Component**: `src/WinVelo.App/Views/DashboardTabView.xaml`, `src/WinVelo.App/ViewModels/DashboardViewModel.cs`, `src/WinVelo.Core/Interfaces/IPlatformServices.cs`, `src/WinVelo.Platform/WindowsRestoreService.cs`, `src/WinVelo.Simulation/SimulationServices.cs`, `src/WinVelo.Modules/Tweaks/TweakService.cs`
- **Finding**:
  1. The Home tab featured animated 1-second live RAM polling (`DispatcherTimer`), CPU/RAM telemetry meters, and a "Free Up RAM (Purge Cache)" button that functioned as placebo decoration.
  2. Essential system security and servicing states — admin status, System Restore status with latest restore point creation timestamp, exact count of tweaks applied by WinVelo, servicing health-check summary, and pending reboot / explorer restart notifications — were either absent or buried.
  3. Pending reboots and explorer restarts caused by applied tweaks were not surfaced to users on the dashboard.
- **Root Cause**: Reliance on decorative monitoring widgets instead of calm, transparent, and actionable system health indicators.
- **Fix**:
  1. Redesigned Home tab with 4 calm status tiles: Admin Privileges, System Restore with latest restore point date, "N tweaks applied by WinVelo" (active journal count), and Servicing Health status.
  2. Added prominent contextual alert banners for pending reboots and pending explorer restarts (with one-click "Restart Explorer" command).
  3. Added `GetLatestRestorePointDate()` to `ISystemRestoreService`, `WindowsRestoreService`, and `FakeRestoreService`.
  4. Added `IsRebootPending()` and `IsExplorerRestartPending()` to `ITweakService` and `TweakService` querying component-based servicing, session manager rename operations, and active journal requirements.
  5. Completely removed the 1-second background `_ramMonitorTimer`, pulsing animations, RAM progress bar / meters, and the RAM purge button.
  6. Added comprehensive unit test coverage in `HomeCalmStatusTests.cs`.

---

## BUG-030: Absence of First-Run Onboarding Wizard, Unverified System Restore Baseline, and Lack of Simple vs Advanced Modes
- **Component**: `src/WinVelo.Core/Models/UserSettings.cs`, `src/WinVelo.Core/Interfaces/IAppSettingsService.cs`, `src/WinVelo.Platform/AppSettingsService.cs`, `src/WinVelo.App/ViewModels/FirstRunWizardViewModel.cs`, `src/WinVelo.App/Views/FirstRunWizardView.xaml`, `src/WinVelo.App/ViewModels/MainViewModel.cs`, `src/WinVelo.App/Views/MainWindow.xaml`, `src/WinVelo.Core/Interfaces/IPlatformServices.cs`, `src/WinVelo.Platform/WindowsRestoreService.cs`, `src/WinVelo.Simulation/SimulationServices.cs`, `src/WinVelo.Tests/FirstRunWizardTests.cs`
- **Finding**:
  1. The application launched directly into the full tweaking surface without explaining what WinVelo does and does not do, creating confusion for new or non-technical users.
  2. Users lacked an upfront onboarding safety check to enable System Restore and create a verified baseline restore point before applying system changes.
  3. No option existed to choose between a curated "Simple" mode (only recommended tweaks displayed) and an "Advanced" mode (all tweaks with experimental settings behind a dedicated toggle).
  4. Preferences and first-run completion status were not persisted in settings.
- **Root Cause**: Missing dedicated first-run onboarding wizard flow and absence of persistent user settings model.
- **Fix**:
  1. Implemented `UserSettings` model and `IAppSettingsService` / `AppSettingsService` persisting configuration to `%APPDATA%\WinVelo\settings.json` (or sandboxed path in test mode).
  2. Implemented `FirstRunWizardViewModel` and `FirstRunWizardView.xaml` with 3 clear steps:
     - Step 1: Plain language summary of what WinVelo does and does not do (3 short honest bullets).
     - Step 2: System Restore detection, enabling for C:\ if disabled (`EnableSystemRestore`), and creating/verifying restore point creation against the system restore point list.
     - Step 3: Experience mode selection ("Simple" for Recommended tweaks only vs "Advanced" for full catalog with experimental toggles).
  3. Integrated wizard modal overlay in `MainWindow.xaml` and `MainViewModel.cs`, auto-displaying on first run and accessible on demand via a "Setup Wizard" button in the navigation sidebar.
  4. Added `EnableSystemRestore(string driveLetter)` to `ISystemRestoreService`, `WindowsRestoreService` (via PowerShell `Enable-ComputerRestore`), and `FakeRestoreService`.
  5. Added comprehensive test coverage in `FirstRunWizardTests.cs` covering all wizard steps, restoration validation, mode selection, and reopenability.
---

## BUG-031: Tweaks Page Usability Gaps: Divergent Execution Dialogs, Missing Risk/State Filter Suite, and Lack of Instant Apply Undo Toasts
- **Component**: `src/WinVelo.App/ViewModels/UnifiedExecutionDialogViewModel.cs`, `src/WinVelo.App/Views/UnifiedExecutionDialogView.xaml`, `src/WinVelo.App/ViewModels/TweaksViewModel.cs`, `src/WinVelo.App/Views/TweaksTabView.xaml`, `src/WinVelo.App/ViewModels/DashboardViewModel.cs`, `src/WinVelo.App/ViewModels/MainViewModel.cs`, `src/WinVelo.App/Views/MainWindow.xaml`, `src/WinVelo.Tests/EngineTests/CompositeStateDetectionTests.cs`
- **Finding**:
  1. The UI maintained fragmented, disparate dialog implementations for preset previews, multi-select apply, single tweak execution, and rollback reporting.
  2. Safe and Low risk tweaks required redundant confirmation steps or failed to provide immediate undo capability via non-intrusive toast notifications.
  3. Medium and High risk tweaks risked accidental single-click execution rather than mandating an explicit preview/confirm modal with safety notices and servicing warnings.
  4. The tweaks catalog lacked dedicated filter dropdowns for Risk (Safe, Low, Medium, High) and Detection State (Applied, Partial, Not applied, Can't verify).
  5. State badges used inconsistent naming instead of clean, human-readable labels (`Applied`, `Partial`, `Not applied`, `Can't verify`), and risk color chips lacked high-contrast WCAG-compliant styling.
- **Root Cause**: Ad-hoc UI dialogs developed independently across tabs without a single unified preview/execution modal component and missing toast notification workflow.
- **Fix**:
  1. Implemented `UnifiedExecutionDialogViewModel` and `UnifiedExecutionDialogView.xaml`:
     - Serves as the single shared modal preview dialog (supporting batch selection, item toggling, reversible status, reboot indicators, current state, and risk color chips).
     - Serves as the single shared execution result dialog (displaying itemized outcomes, failure reasons, and Windows servicing health check warnings).
  2. Refactored `TweaksViewModel`:
     - Safe and Low risk tweaks apply immediately and trigger a calm, non-blocking toast banner with a direct "Undo" action.
     - Medium and High risk tweaks never apply on a single click; they open the unified preview dialog requiring explicit confirmation.
     - Added comprehensive filter controls: Risk filter, State filter, Search box, Category selector, "Recommended only", and "Show experimental".
     - Standardized state badge labels and risk color chips across collapsed and expanded cards.
  3. Integrated `UnifiedExecutionDialogView` into `MainWindow.xaml` and wired it into `DashboardViewModel`, `TweaksViewModel`, and `MainViewModel`.
---

## BUG-032: History Page Gaps: Missing Unified Rollback Integration, Unsorted Sessions, and Buried System Restore Points
- **Component**: `src/WinVelo.App/ViewModels/HistoryViewModel.cs`, `src/WinVelo.App/Views/HistoryTabView.xaml`, `src/WinVelo.Core/Interfaces/IPlatformServices.cs`, `src/WinVelo.Platform/WindowsRestoreService.cs`, `src/WinVelo.Simulation/SimulationServices.cs`, `src/WinVelo.App/ViewModels/MainViewModel.cs`, `src/WinVelo.Tests/HistoryPageTests.cs`
- **Finding**:
  1. The History page did not guarantee that journal sessions were sorted newest first, creating confusion when tracking recent modifications.
  2. The page displayed raw, ungrouped journal action records without categorizing them by tweak name or surfacing tweak-level status (Active vs Reverted).
  3. Rollback operations (single tweak undo, session rollback, and "Undo all WinVelo changes") executed without integrating into the unified preview/result dialog workflow, and "Undo all WinVelo changes" lacked the explicit notice that modifications by other tools are not covered.
  4. System restore points were only displayed as a raw count in a small footer rather than as a dedicated, inspectable read-only list with timestamps, sequence numbers, and descriptions.
  5. Session log export did not persist to local disk exports directory with accessible viewing.
- **Root Cause**: History page was treated as a basic log viewer rather than a comprehensive, safe, and transparent rollback management center.
- **Fix**:
  1. Updated `HistoryViewModel` to sort all journal sessions strictly newest first (`OrderByDescending(s => s.StartedAtUtc)`).
  2. Implemented `HistoryTweakItemModel` grouping action records per tweak with distinct status badges (`Active`, `Reverted`), action counts, and individual "Undo Tweak" buttons.
  3. Integrated all rollback actions (`UndoTweak`, `UndoSession`, and `UndoAllChanges`) with `UnifiedExecutionDialogViewModel`:
     - Added explicit preview/confirm dialog with safety notice ("This will revert all tweaks recorded in the WinVelo rollback journal. Changes made by other tools or Windows Settings are not covered.").
     - Unified execution reporting with itemized results and servicing health verification.
  4. Extended `ISystemRestoreService`, `WindowsRestoreService`, `FakeRestoreService`, and `NullRestoreService` with `GetRestorePoints()` returning `RestorePointItem` objects; added a dedicated read-only restore points view on the History tab.
  5. Enhanced session log export to generate clean `.txt` files in `%LOCALAPPDATA%\WinVelo\Exports\` alongside an in-app viewer dialog.
  6. Added unit test coverage in `HistoryPageTests.cs` validating newest-first ordering, trigger source detection, grouped tweak statuses, unified preview workflows, restore point lists, and text export formatting.
---

## BUG-033: Quality Bar: Outdated Monolithic UI Test Suite, Unchecked Startup Latency, and Outdated Legacy Documentation
- **Component**: `src/WinVelo.UiTests/WinVeloUiTests.cs`, `src/WinVelo.App/Views/FirstRunWizardView.xaml`, `docs/screenshots/`, `README.md`
- **Finding**:
  1. The FlaUI automation suite targeted obsolete 8-tab navigation IDs (`Tab_Config`, `Tab_Toolbox`, `Tab_Backups`) rather than the modern 5-tab structure (`Home`, `Tweaks`, `Apps`, `Tools`, `History`) and lacked dedicated test coverage for the preview → apply → instant undo-toast workflow.
  2. Automated testing risked interference from the first-run setup wizard overlay due to missing automation IDs on setup buttons.
  3. Documentation in `README.md` contained outdated descriptions referencing 8 tabs and exaggerated performance claims ("Max FPS Boost", "Everyday Speed & Clean Setup").
  4. Documentation screenshots in `docs/screenshots/` reflected obsolete UI tabs and widgets.
- **Root Cause**: Fast architectural evolution across Phases 2 and 3 without updating the high-level UI automation harness and visual artifacts.
- **Fix**:
  1. Refactored `WinVeloUiTests.cs` into modular tests covering all 5 tabs and dedicated flows:
     - `Test_01_HomePage_LoadsCalmStatus`
     - `Test_02_TweaksPage_LoadsFiltersAndCatalog`
     - `Test_03_AppsPage_LoadsCatalogAndSearch`
     - `Test_04_ToolsPage_SwitchesSubTools`
     - `Test_05_HistoryPage_DisplaysSessionsAndRestorePoints`
     - `Test_06_Flow_PreviewApplyAndUndoToast`
     - `FullAuditUiTestSuite_All5TabsAndControls`
     Strictly verified that all tests launch the application with `--test-mode`.
  2. Added `AutomationProperties.AutomationId` and `Name` to `FirstRunWizardView` controls (`SkipWizardButton`, `WizardNextButton`, `WizardFinishButton`) ensuring clean accessibility and automated dismissal.
  3. Measured cold start startup latency to interactive Home in simulated test mode: 1013ms – 1570ms, well below the 2-second quality ceiling.
  4. Retook all documentation screenshots cleanly in test mode with `--test-mode --capture-screenshots docs/screenshots`.
  5. Rewrote `README.md` with transparent, neutral descriptions of the 5-tab architecture and zero exaggerated performance claims.

---

## BUG-034: Category Presets Mixed Personal Preferences, Problem-Specific Fixes and Conflicting Tweaks
- **Component**: `tools/PresetGenerator/Program.cs`, `Catalog/Presets/*.json`, `Catalog/Tweaks/*.json`, `Catalog/schema/tweak.schema.json`
- **Finding**:
  1. Topic presets included every tweak with a matching tag, so applying "Gaming" or "Clean UI" also changed personal choices (dark mode, taskbar alignment, seconds in the clock) and applied problem-specific fixes (MPO disable, S3 sleep, verbose logon) on machines without that problem.
  2. Presets could contain two tweaks that write the same registry value with different data (`EnableActivityFeed`, `OverlayTestMode`, `SearchboxTaskbarMode`, `DODownloadMode`), so the final state depended on apply order.
  3. Several preference toggles had inverted or misleading names and explanations (for example a "Disable" title on a tweak that enables the setting).
  4. A previous report claimed the MPO tweaks were excluded as needsReview; they were not (both had `needsReview: false` and were inside presets).
  5. "Recommended Baseline" described itself as not changing how Windows looks, while it included visual tweaks.
- **Fix**:
  1. Added `presetRole` (`optimization`, `preference`, `fix`, `action`, `advanced`) to the schema, model and all catalog tweaks. Only `optimization` tweaks are eligible for presets; the others are listed under "Not included" with a reason.
  2. The generator now detects registry/service target conflicts deterministically (alphabetical, first wins) and excludes the loser with a "Conflicts with ..." reason.
  3. Recommended Baseline is Safe-only from the privacy/ui/ai/apps topics, with an accurate description.
  4. Corrected names, descriptions and explanations of the affected preference toggles and fixed wrong topic tags.
  5. Added 15 commonly requested tweaks (Edge sidebar/startup boost/background mode policies, Click to Do, Recall availability, Paint and Notepad AI, Widgets policy, File Explorer sync ads, OneDrive navigation entry, last-active taskbar click, Find My Device, Fast Startup, AutoEndTasks, startup app delay). All are marked `verifiedOnBuild: "unverified"` until tested on a real machine.

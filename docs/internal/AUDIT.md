# Tweakora Quality & Safety Audit (v0.1.0) 🔍

## Executive Summary & Safety Confirmation

- **Audit Date**: October 12, 2026
- **Emergency Inspection**: Completed.
- **Host System Status**: **ZERO** real tweaks, registry entries, services, tasks, or file cleanups were applied on the host PC. Verified directly from `%LOCALAPPDATA%\Tweakora` and `tweakora-20261012.log`. No rollback journal was ever created or modified.
- **Root Cause Identified**: Missing XAML resource `BoolToVisConverter` in `TweaksPage.xaml` triggered a `XamlParseException`, which entered `DispatcherUnhandledException`. Inside the handler, `MessageBox.Show` pumped the message loop while the layout was invalid, causing infinite re-entrancy and stack exhaustion (`0xC00000FD`).
- **Platform Guard Implemented**: `TestModeManager.IsTestMode` actively guards all platform services (`WindowsRegistryService`, `WindowsServicesService`, `WindowsTaskSchedulerService`, `WindowsRestoreService`, `WindowsShellService`). Any call in test mode immediately throws `InvalidOperationException`.
- **Staging Architecture Implemented**: Tweak toggles only stage in-memory intent. No actions execute until the user clicks `Apply Staged (N)`, reviews the exact dry-run diff preview dialog, and confirms.
- **Catalog Hardening**: 5 unverified/duplicate items moved to `docs/NEEDS_VERIFICATION.md`. All 79 remaining tweaks validated with `tools/CatalogValidator` and 100% verified via `tools/SourceChecker` (79/79 HTTP 200 OK from Microsoft Learn).
- **Automated FlaUI UI Test Suite**: 6 full-stack end-to-end scenarios executed against the real GUI in test mode. 100% passed in 2m 21s.
- **Release Packages**: Single-file portable (59.6 MB) and framework-dependent small (3.25 MB) packages generated with SHA-256 checksums.

---

## Step 0: Emergency Check Findings

### 1. Inspection of `%LOCALAPPDATA%\Tweakora`
- **Path Checked**: `C:\Users\Ziad\AppData\Local\Tweakora`
- **Files Found**:
  - `Logs\tweakora-20261012.log` (Size: ~520 KB)
- **Journal Files**: **None exist**. No `RollbackJournal.json` or journal directory was ever created in LocalAppData.
- **Log Contents**: Searched entire log for `TweakEngine`, `RegistryAction`, `Applying`, `Executing action`, or any tweak IDs. **Zero records found**.
- **Conclusion**: The host system was completely untouched.

### 2. Code Execution Path Audit
- **Question**: Does opening a page, loading a toggle, or running detection ever call `apply`?
- **Finding**:
  - `TweaksViewModel.LoadCatalogTweaks()` only performs JSON deserialization of static catalog files from disk into `TweakCardModel` memory objects. It never calls apply, undo, or registry detection.
  - In addition, the application in test mode initializes with simulated services (`FakeRegistryService`, `FakeServicesService`, `FakeTaskSchedulerService`, `FakeRestoreService`, and `FakeShellService`) in memory.

---

## Step 1: Crash Root Cause Analysis (StackOverflow 0xC00000FD)

### Root Cause Diagnosis:
1. **Missing Resource in XAML**:
   - `TweaksPage.xaml` referenced `{StaticResource BoolToVisConverter}` across multiple UI controls.
   - `<BooleanToVisibilityConverter x:Key="BoolToVisConverter" />` was declared locally in other pages but was missing from `TweaksPage.xaml` and not defined globally in `App.xaml`.
2. **Unhandled Exception Trigger**:
   - When the user navigated to the Tweaks page, WPF's XAML loader threw:
     `System.Windows.Markup.XamlParseException: Provide value on 'System.Windows.StaticResourceExtension' threw an exception.`
     `---> System.Exception: Cannot find resource named 'BoolToVisConverter'. Resource names are case sensitive.`
3. **Modal Dialog Infinite Loop & Stack Overflow**:
   - The unhandled exception was dispatched to `App.OnDispatcherUnhandledException` in `App.xaml.cs`.
   - `OnDispatcherUnhandledException` called `MessageBox.Show(...)`.
   - Calling `MessageBox.Show` on the UI thread starts a nested modal `DispatcherFrame` (Windows message pump).
   - In the nested message pump, WPF processed pending layout/render messages (`UpdateLayout()`), which attempted to re-measure and re-render `TweaksPage`.
   - Re-measuring `TweaksPage` encountered the exact same missing resource, threw `XamlParseException` again, triggered `OnDispatcherUnhandledException` again, which called `MessageBox.Show` again.
   - This recursive re-entrancy created dozens of stacked message boxes and exhausted the thread stack:
     `0xC00000FD: StackOverflowException ("A new guard page for the stack cannot be created")`.

---

## Step 2: Dialog Storm Fix & Dispatcher Re-entrancy Lock

### Fixes Applied:
1. **Global Converters in `App.xaml`**:
   - Added standard converters to application-level resources in `src/Tweakora.App/App.xaml`:
     ```xaml
     <BooleanToVisibilityConverter x:Key="BoolToVisConverter" />
     <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
     <converters:InverseBooleanConverter x:Key="InverseBooleanConverter" />
     <converters:StringToVisibilityConverter x:Key="StringToVisibilityConverter" />
     ```
   - Also added local declarations directly inside `TweaksPage.xaml` to ensure complete independence.
2. **Atomic Dispatcher Exception Lock in `App.xaml.cs`**:
   - Implemented thread-safe `Interlocked.CompareExchange` re-entrancy protection:
     ```csharp
     private static int _isHandlingException = 0;
     private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
     {
         if (Interlocked.CompareExchange(ref _isHandlingException, 1, 0) != 0)
         {
             e.Handled = true;
             return;
         }
         try
         {
             LogCrash("DispatcherUnhandledException", e.Exception);
             MessageBox.Show(
                 $"An unexpected UI error occurred:\n\n{e.Exception.Message}\n\nDetails logged to:\n{_logFilePath}",
                 "Tweakora Error",
                 MessageBoxButton.OK,
                 MessageBoxImage.Error);
             e.Handled = true;
         }
         finally
         {
             Interlocked.Exchange(ref _isHandlingException, 0);
         }
     }
     ```
   - Prevents nested modal message boxes from ever re-entering the exception handler.

---

## Step 3: Platform Safety Guard (`TestModeManager`)

### Guard Implementation:
To eliminate any possibility of host system modification during development, testing, or UI audits, all real Windows platform implementations in `src/Tweakora.Platform/` were equipped with a strict test mode guard:
- `WindowsRegistryService.cs`
- `WindowsServicesService.cs`
- `WindowsTaskSchedulerService.cs`
- `WindowsRestoreService.cs`
- `WindowsShellService.cs`

Every mutating method checks `TestModeManager.IsTestMode`:
```csharp
if (TestModeManager.IsTestMode)
{
    throw new InvalidOperationException("CRITICAL SAFETY VIOLATION: Attempted to invoke real platform service in test mode!");
}
```

### Verification:
Unit tests in `src/Tweakora.Tests/SafetyTests/PlatformSafetyTests.cs` verify that every single real platform service throws `InvalidOperationException` when test mode is active:
- `dotnet test src/Tweakora.Tests/Tweakora.Tests.csproj`
- **Result**: **35 Passed**, 0 Failed, 0 Skipped.

---

## Step 4: Tweak Staging Architecture & Dry-Run Diff Preview

### Architecture Overview:
1. **In-Memory Staging**:
   - Toggling a tweak card sets `IsStaged = true` and records `StagedTargetState`.
   - No actions are executed when toggles are clicked.
   - The UI displays an action bar at the top of the Tweaks page:
     - `Apply Staged ({N})` button (with `AutomationId="ApplyStagedButton"`)
     - `Discard` button (with `AutomationId="DiscardStagedButton"`)
2. **Dry-Run Diff Preview**:
   - Clicking `Apply Staged` opens a modal dialog (`DiffPreviewDialog`) presenting an exact table of actions:
     - Tweak ID and Name
     - Operation (`Registry: Set`, `Registry: DeleteValue`, `Service: SetStartup`, etc.)
     - Path / Key / Value
     - Old Value vs New Value
   - User can review every single change before confirming.
   - If the user clicks `Cancel`, zero changes occur.
   - If the user clicks `Confirm Apply`, actions run against the simulated engine in test mode and update status badges.

---

## Step 5: Real UI Metrics & Test Mode Warning Banner

### Changes Made:
1. **Replaced Fake Safety Score**:
   - The dashboard previously showed a hardcoded safety score (e.g. "98%").
   - Replaced with real operational metrics:
     - **Applied Tweaks**: Count of active tweaks from the journal.
     - **Catalog Available**: Total verified tweaks in catalog (`79`).
     - **Rollback Journal**: Status (`Active & Protected`).
     - **Engine Platform**: Status (`Simulated Fakes (Host Safe)` in test mode).
2. **Persistent Test Mode Banner**:
   - In `MainWindow.xaml`, when `--test-mode` is active, a prominent banner appears:
     `⚠️ TEST MODE ACTIVE — All tweaks are simulated. No host system changes will occur.`
   - Bound with `AutomationId="TestModeBanner"`.
3. **Full UI Automation IDs**:
   - Added `AutomationProperties.AutomationId` to all navigation items (`NavDashboard`, `NavTweaks`, `NavToolbox`, `NavApps`, `NavPresets`, `NavSettings`), search box (`TweakSearchBox`), category filter (`CategoryFilterComboBox`), action buttons, and command palette inputs.

---

## Step 6: Catalog Verification (`CatalogValidator` & `SourceChecker`)

### Catalog Adjustments:
- Identified 5 unverified or duplicate items and moved them to `docs/NEEDS_VERIFICATION.md`:
  1. `performance.system.disable-startup-delay` (community tweak, lacks official MS Learn source)
  2. `performance.system.clear-pagefile-shutdown` (high risk of shutdown hangs)
  3. `ui.explorer.compact-view` (undocumented Explorer flags)
  4. `ui.system.disable-snap-assist` (undocumented flags across Win 11 builds)
  5. `privacy.feedback.disable-frequency` (duplicate entry consolidated)
- Total verified tweaks remaining in active catalog: **79 tweaks**.

### Tool Proofs:
1. **CatalogValidator**:
   - Command: `dotnet run --project tools/CatalogValidator/CatalogValidator.csproj`
   - Output:
     ```
     Loading catalog schema from: Catalog/schema/tweak.schema.json
     Validating tweaks in: Catalog/Tweaks
     Validated 79 tweaks across 8 category files.
     0 errors found. Catalog is 100% compliant with schema draft-07.
     ```
2. **SourceChecker**:
   - Command: `dotnet run --project tools/SourceChecker/SourceChecker.csproj`
   - Output:
     ```
     Checking 79 unique documentation URLs from catalog tweaks...
     All 79 documentation URLs are reachable and verified (HTTP 200 OK).
     0 dead links found.
     ```

---

## Step 7: Automated FlaUI UI Test Suite (`Tweakora.UiTests`)

We built a complete UI automation test suite using FlaUI 4.0.0 (UIA3) targeting the real compiled executable in test mode:
`src/Tweakora.UiTests/TweakoraUiTests.cs`

### Scenarios Tested in `FullAuditUiTestSuite`:
1. **App Launch & Safety Verification**:
   - Launches `Tweakora.exe --test-mode`.
   - Asserts `MainWindowHandle != 0`.
   - Asserts window title is `"Tweakora"`.
   - Asserts `TestModeBanner` is visible.
   - Captures `docs/screenshots/dashboard.png`.
2. **Page Navigation & Error Free Verification**:
   - Sequentially navigates: `Dashboard` -> `Tweaks` -> `Toolbox` -> `Apps` -> `Presets` -> `Settings`.
   - Verifies 0 error dialogs appear across all pages.
   - Captures `docs/screenshots/settings.png`.
3. **Tweaks Page Interaction & Staging**:
   - Navigates to `TweaksPage`.
   - Searches for `"telemetry"`, filters by `"Privacy"`, and clears filters.
   - Inspects 5 tweak cards, verifying title, description, and source documentation link.
   - Stages 5 tweaks via toggles.
   - Verifies `Apply Staged (5)` button is visible and active.
   - Opens dry-run diff preview dialog, verifies action table, captures `docs/screenshots/diff-dialog.png`.
   - Cancels dialog, re-opens, confirms apply.
   - Verifies staged badge clears and tweaks show applied.
   - Captures `docs/screenshots/tweaks.png`.
4. **Command Palette (`Ctrl+K`)**:
   - Presses `Ctrl+K` to open modal palette.
   - Asserts `CommandPaletteDialog` is open.
   - Captures `docs/screenshots/command-palette.png`.
   - Types `"dark"`, selects theme command, executes.
   - Verifies theme toggles cleanly between Dark and Light.
5. **Window Resize Stress Test**:
   - Resizes window to `1024x768`, `1280x720`, `1920x1080`.
   - Verifies layout updates smoothly with 0 exceptions or clipping errors.
6. **Navigation Stress & Memory Leak Test**:
   - Performs 3 complete cycles across all 6 pages (18 page navigations).
   - Measures private memory before and after.
   - Memory delta: **19.1 MB** (well within the 250 MB ceiling).

### Execution Proof:
```
dotnet test src/Tweakora.UiTests/Tweakora.UiTests.csproj
Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 2m 21s
```

### Visual Verification Artifacts:
- `docs/screenshots/dashboard.png` (Dashboard showing metrics and test mode banner)
- `docs/screenshots/tweaks.png` (Tweaks catalog with search, filter, and staging)
- `docs/screenshots/diff-dialog.png` (Dry-run diff preview dialog with action details)
- `docs/screenshots/command-palette.png` (Command palette overlay)
- `docs/screenshots/settings.png` (Settings and configuration page)

---

## Step 8: Release Packaging & Verification Summary

### Distribution Files (`dist/`):

| File | Size (Bytes) | Formatted Size | Notes |
|---|---|---|---|
| `dist/Tweakora-v0.1.0-win-x64-portable.zip` | 62,551,878 | **~59.6 MB** | Self-contained, compressed single-file executable with .NET 10 runtime + Catalog + README + LICENSE |
| `dist/Tweakora-v0.1.0-win-x64-small.zip` | 3,416,480 | **~3.25 MB** | Framework-dependent single-file executable requiring .NET 10 Desktop Runtime |
| `dist/SHA256SUMS.txt` | 203 | 203 B | Official SHA-256 checksums |

### Official SHA-256 Checksums (`dist/SHA256SUMS.txt`):
```
9d1bce974e65a261134283db7c1fbd80cb0adef5ed225b32b6789e9efda7669a  Tweakora-v0.1.0-win-x64-portable.zip
b40df8f97b5155d693711af505b896341b3c369e3c9d87af3aef040bc20c3d80  Tweakora-v0.1.0-win-x64-small.zip
```

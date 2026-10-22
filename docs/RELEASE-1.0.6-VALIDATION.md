# WinVelo 1.0.6 validation — 2026-10-23

## Reproduced defects

Before the fixes, all four original regression cases failed:
- A native read-only command called with a blocked UI synchronization context could not complete.
- The same deadlock affected PowerShell execution.
- Servicing health detection captured the UI context and could deadlock after Apply.
- The Startup view bound to a nonexistent collection and command.

The native-process continuations now avoid capturing the caller context. Servicing reads/repairs run on workers. Startup uses FilteredApps and ToggleAppCommand with a one-way checkbox, a loading guard, and refresh controls.

## Completed checks

- Release unit/integration suite: **435 passed, 0 failed, 0 skipped**.
- Debug functional UI suite: **11 passed, 0 failed**. Includes profile Apply completion and subsequent navigation; Startup list, a physical checkbox click, refresh persistence; five-page navigation; history; accessibility; first-run wizard.
- Additional final targeted UI run: **5 passed**, covering Apply, Startup, tools, history and accessibility.
- Release solution build with TreatWarningsAsErrors=true: **0 warnings, 0 errors**.
- Catalog validator: **142 tweaks, 6 presets, 242 apps, 0 errors**.

Regression process tests use harmless echo/sleep commands and isolated fake services. UI Apply and Startup changes run with --test-mode against simulated platform services. They do not apply real tweaks to the host PC.

Commands used:

```powershell
dotnet test src/WinVelo.Tests/WinVelo.Tests.csproj -c Release
dotnet test src/WinVelo.UiTests/WinVelo.UiTests.csproj -c Debug --filter "FullyQualifiedName!~Test_09"
dotnet build WinVelo.sln -c Release -p:TreatWarningsAsErrors=true
dotnet run --project tools/CatalogValidator/CatalogValidator.csproj -c Release --no-build
```

Test_09 examines archived marketing screenshots, rather than runtime behavior, and was excluded from the functional UI run. The separate computer-use helper failed to initialize with 'failed to write kernel assets'; the repository's FlaUI tests exercised the actual WPF UI instead.

## Scope

These checks establish that the reproduced deadlocks and Startup binding failures are fixed in the tested paths. They do not prove every tweak succeeds on every Windows build, driver, antivirus configuration or permission level. Broad real-mode Windows VM validation remains separate work. No claim of a completely error-free application is made.

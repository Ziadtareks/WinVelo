# WinVelo audit report (v1.0.2)

Branch: `claude/quirky-einstein-l1yxvx` (22 commits on top of `f32c400`, tag v1.0.1). Audit date: 2026-10-19.

## 1. Summary

WinVelo's design is sound (write-ahead journal, simulation fakes, evidence labels), but the audit found real problems in the parts users trust most. Undo could delete more than WinVelo added, leave settings changed when two tweaks shared a value, or lose the whole journal in a crash. The app installer and the DNS/feature tools put catalog values into PowerShell, so a crafted catalog entry could run commands as administrator. The "Windows Update is never disabled" promise was enforced only at build time and was broken by the app's own update mode. All of these are fixed and covered by new tests (from 247 to 353 tests). The catalog itself is in good shape, but 3 of the 36 winget ids did not exist.

## 2. Issues found

Severity: Critical / High / Medium / Low. Status: Fixed / Not fixed / Needs human decision.

| ID | Severity | Area | File(s) | Status | Description |
|---|---|---|---|---|---|
| A-01 | Critical | Security | `Modules/Apps/AppInstallService.cs` | Fixed | Package ids were put into a PowerShell command inside double quotes, so `$(...)` in a catalog entry would run as administrator. Now `ProcessStartInfo.ArgumentList` and strict id formats. |
| A-02 | High | Safety / journal | `Core/Engine/RollbackJournalManager.cs` | Fixed | Journal saves truncated the file and then wrote it. A crash or power cut during a save left a cut-off file, which was silently skipped, so its changes could no longer be undone. Saves are now atomic (temp file, flush, `File.Replace` with a `.bak` copy). |
| A-03 | High | Safety / journal | same | Fixed | Two processes (app and CLI, or two windows) could write the journal at once. Writes are now serialised by a named mutex. Damaged files are reported, never deleted. |
| A-04 | High | Safety / undo | `Core/Engine/Handlers/RegistryActionHandler.cs` | Fixed | Undoing a tweak that created a key ran a recursive `DeleteKey`, which also removed values added later by other tweaks, Group Policy or programs. |
| A-05 | High | Safety / undo | `Core/Engine/TweakRunner.cs` | Fixed | 21 registry values are set by more than one tweak. Undoing the older tweak first restored the original value while the other tweak was still applied; undoing the second one then "restored" the first tweak's value, so the setting stayed changed. The original value is now handed over to the newer tweak's record. |
| A-06 | High | Safety | `Core/Safety/ServicingGuard.cs` (new), handlers, `CatalogLoader` | Fixed | Protection of Windows Update, BITS, Store and Defender existed only in CatalogValidator (build time). A hand-edited Catalog folder could disable them. Now enforced at catalog load and again in every handler. |
| A-07 | High | Safety | `Modules/Updates/UpdateModeService.cs`, `Modules/Services/ServicesManagerService.cs` | Fixed (owner decision: block everywhere) | "Disable All Updates" disabled `wuauserv`, `BITS` and `UsoSvc` outside the journal, which contradicted README/SECURITY. It is now policy-only (`NoAutoUpdate`) and repairs services an older version disabled. The Services tool refuses protected services. |
| A-08 | High | Safety / CLI | `Cli/Program.cs` | Fixed | `winvelo-cli --self-test` switched to real mode by itself and changed 53 HKCU settings without `--real-mode` or a prompt. |
| A-09 | High | Security | `Modules/Network/*`, `Modules/Tweaks/TweakService.cs`, `Modules/Config/FeatureService.cs` | Fixed | DNS addresses and optional-feature names were put into single-quoted PowerShell (`'1.1.1.1'; <cmd>; '`). `SafeCommands` now builds these commands from parsed IPs and plain names only. |
| A-10 | High | Security | `App/ViewModels/InstallViewModel.cs`, `Platform/WindowsShellService.cs` | Fixed | The "website" button shell-executed any catalog string (a local exe or `file:` URL would start as administrator). Only http(s) links open now (`SafeUrl`). |
| A-11 | High | Catalog loading | `Modules/Tweaks/TweakService.cs`, `Cli/Program.cs`, others | Fixed | The app needed a `Catalog` folder next to the exe and silently showed an empty list if it was missing or one file was broken (`catch { }`). The catalog is now embedded; a broken file or tweak is skipped and reported (EN/AR). |
| A-12 | Medium | Safety / undo | `RegistryActionHandler.cs` | Fixed | The default value (`@`) was never recorded as existing: undo deleted a default value that existed before and never removed one WinVelo created. |
| A-13 | Medium | Honesty | `RegistryActionHandler.cs` | Fixed | "Access denied" (Tamper Protection, TrustedInstaller) was reported as a successful apply/undo. |
| A-14 | Medium | Safety / undo | `RegistryActionHandler.cs`, `Platform/WindowsRegistryService.cs` | Fixed | REG_MULTI_SZ values were saved as `System.String[]` and `SetValue` could not write them back; REG_NONE came back as DWORD. |
| A-15 | Medium | Apps | `applications.json` | Fixed | Checked against microsoft/winget-pkgs (master, 2026-10-19): `dotPDNLLC.paintdotnet` → `dotPDN.PaintDotNet`, `AnyDeskSoftwareGmbH.AnyDesk` → `AnyDesk.AnyDesk`, `WhatsApp.WhatsApp` (not in winget-pkgs) → Store id `9NKSQGP7F2NH` (see section 4). |
| A-16 | Medium | Apps | `AppInstallService.cs` | Fixed | Success was decided by searching the output for "Error"/"Failed". Exit codes are now used, with clear messages for missing winget/choco, timeout, not found and offline. |
| A-17 | Medium | Apps / UX | `InstallViewModel.cs`, `InstallTabView.xaml` | Fixed | The Apps page never displayed its status message (failures were invisible) and installs could not be cancelled. |
| A-18 | Medium | Privacy | `InstallViewModel.cs`, `PRIVACY.md` | Fixed | `winget list` (which may refresh online sources) ran at app startup, contradicting "zero network requests during startup". Now runs when the Apps page opens; PRIVACY.md says so. No other network code exists (no HttpClient, telemetry or update check in the app). |
| A-19 | Medium | Privacy | `Core/Diagnostics/DiagnosticsCollector.cs` | Fixed | The bundle only removed the current user name, profile path and machine name. Now also other profile paths, domain, SIDs, e-mail, IPv4/IPv6, MAC and serial numbers; names are replaced as whole words. |
| A-20 | Medium | Elevation | `App/app.manifest`, `App.xaml.cs`, `AppAdminGuard.cs` | Fixed | `requireAdministrator` made `--test-mode` show a UAC prompt (and blocked unattended UI tests). Now `asInvoker` plus one elevation request in normal mode. |
| A-21 | Medium | Restore points | `Platform/WindowsRestoreService.cs` | Fixed | Unescaped description (apostrophe broke it), 30 s timeout (`ExitCode` of a running process threw), possible pipe deadlock, unclear 24-hour message. |
| A-22 | Medium | Backups | `TweakService.cs`, `WinVeloStoragePaths.cs` | Fixed | `.reg` backups went to `%TEMP%`, which WinVelo's own cleaner empties. |
| A-23 | Medium | Release | `.github/workflows/release.yml` | Fixed | The signing step signed a path that did not exist yet (before `publish.ps1` ran), so a configured certificate would have had no effect. |
| A-24 | Medium | CI | GitHub repository settings | Needs human decision | No workflow has ever run (also not after this branch's pushes): GitHub Actions appears to be disabled for the repository. |
| A-25 | Medium | Safety / undo | `TweakRunner.cs` | Not fixed | A tweak that is already active when applied (because another tweak set the same values) is skipped without a journal record; undoing it later falls back to the catalog undo actions, which can switch off the other tweak's value. Needs a design for shared targets. |
| A-26 | Medium | Elevation | Platform | Not fixed | If a standard user elevates with a different administrator account, HKCU tweaks go to the administrator's profile. WinVelo does not detect this. Suggest a warning (compare the token user with the Explorer user). |
| A-27 | Medium | Honesty | `Core/Engine/Handlers/ServiceActionHandler.cs` | Needs human decision | Service failures (access denied, service missing) are reported as success with a `[WARN]` text, following WinUtil policy. Registry handling was changed to report failures; services were left as designed. |
| A-28 | Medium | Catalog trust | `CatalogSource.cs`, `App.xaml.cs` | Needs human decision | A `Catalog` folder next to the exe can run PowerShell as administrator (the same as replacing the exe, which is equally writable). WinVelo now tells the user at startup; you may want an explicit opt-in flag instead. |
| A-29 | Low | Rename | whole repo | Fixed | Folders, solution, namespaces and tests still used "Tweakora". Only `AppDataMigrator.LegacyProductName` (migration) and the CHANGELOG mention it now. |
| A-30 | Low | Release | `build/publish.ps1` | Fixed | No per-artifact `.sha256` file; SHA256SUMS lacked the CLI. |
| A-31 | Low | CI | `.github/workflows/build-and-test.yml` | Fixed | Ran only for `main`, no NuGet cache, warnings allowed, CI included UI tests that need a desktop. |
| A-32 | Low | Docs | README, release.ps1, ARCHITECTURE.md | Fixed | Tweak and app counts were typed by hand; now checked by `tools/CatalogStats` in CI. |
| A-33 | Low | Apps | `AppInstallService.cs` | Fixed | Chocolatey could receive a winget id; "Git" showed as installed when only GitHub Desktop was. |
| A-34 | Low | Security | `WindowsRegistryService.cs`, `Cli/Program.cs` | Fixed | `reg.exe export` arguments were concatenated; `--preset <name>` opened `<name>.json` from a path built from the command line. |
| A-35 | Low | Localization | `Lang-ar.json`, views | Fixed | 18 status texts (app badges etc.) had no Arabic; Arabic users had to type English "I understand"; the palette arrow pointed the wrong way in RTL; startup/crash dialogs showed stack traces in English. |
| A-36 | Low | UX | `ToolboxViewModel.cs` | Fixed | The cleanup could not be stopped. The DNS benchmark (bounded to about 20 s) is still not cancellable. |
| A-37 | Low | Repo | `tests/vm/run-vm-tests.ps1` | Fixed | Default VM guest password `Password123!` in the script. |
| A-38 | Low | Repo | git history | Needs human decision | An older commit contains a personal path (`C:\Users\Ziad`); no secrets or tokens were found. Rewriting history is not worth it unless you want it gone. |
| A-39 | Low | Code quality | `Core/Engine/Runners/ScriptRunner.cs` | Not fixed | `RunCmdAsync` (`cmd /c {command}`) is unused but unsafe if someone starts using it. Suggest deleting it. |
| A-40 | Low | Code quality | `Catalog/Apps/apps.winget.json` | Not fixed | Not read by any code (the app uses `applications.json`). Delete or wire it up. |
| A-41 | Low | Code quality | `TweakService.DetectState` | Not fixed | Sync-over-async (`GetAwaiter().GetResult()`); no production caller today. |

Catalog checks that passed (no change needed): unique ids; every documented tweak has an https source and a quote; no anecdotal tweak outside Experimental; no High-risk, irreversible or anecdotal tweak in any preset; no placebo tweaks (the "latency"/"speed up" wording found belongs to accurate network/NTFS descriptions); all 21 shared registry values are either the same data or declared `oppositeOf`.

## 3. Changes by phase

**Phase 1**
- Rename Tweakora → WinVelo (`git mv`), with `%APPDATA%\Tweakora` migration kept and a test that a 1.0.1 journal still loads.
- Catalog embedded in WinVelo.Core (`CatalogSource`, `CatalogLoader`); optional override folder; per-file and per-tweak error handling; EN/AR messages; 11 loader tests.
- `publish.ps1`: `-SkipZip` / `-PackageOnly` stages, `.sha256` per zip and `SHA256SUMS.txt`. Release workflow: version check, CI build, attestation, opt-in SignPath step. README *Verify your download* and *Antivirus false positives*; SIGNING.md and RELEASING.md rewritten.
- CI on every push/PR: .NET 10 on windows-latest, NuGet cache, warnings as errors, unit tests with `WINVELO_TEST_MODE=1`, catalog validator, README counts, vulnerable-package check; UI tests in a non-blocking job; CI badge.
- `tools/CatalogStats` keeps the README/docs counts correct (CI fails on drift).
- App installer rewrite (A-01, A-15 to A-18, A-33), validator rule and 26 tests for ids, links and installer behaviour.

**Phase 2**
- Journal: atomic writes, `.bak` recovery, cross-process mutex, damaged-file reporting (6 tests).
- Registry undo: key deletion only when empty, default value, access denied, multi-string/none, shared-target hand-over (8 tests).
- ServicingGuard enforced in loader, handlers, Services tool; update mode policy-only (owner decision) (30 tests).
- CLI self-test needs `--real-mode`; diagnostics sanitiser; DNS/feature command validation; external catalog warning; `asInvoker` manifest; restore points; backups folder; reg.exe arguments.
- Validator: anecdotal ⇒ Experimental, https source for documented, http(s) sources only.
- Localization: missing Arabic, test covers every `T()` in the app and empty entries, RTL arrow, Arabic confirmation word. New XAML resource test (undefined `StaticResource` keys).
- Cleaner cancel; issue/PR templates; Dependabot; no default VM password; package updates (Mvvm 8.4.2, Microsoft.Extensions 10.0.13, Serilog 4.4.0); CHANGELOG 1.0.2; README top and Arabic section.

## 4. Not verified here, and what you need to do

1. **Turn on GitHub Actions** (Settings › Actions › General › Allow all actions). Nothing has ever run in CI, so the Windows-only tests below have not been seen passing on Windows. Then check the first CI run.
2. **Windows-only tests.** This audit ran on Linux with the WPF runtime borrowed from NuGet. 325 of 353 tests pass; the other 28 need Windows (24 construct WPF view models that call native code, 1 needs `powershell.exe`, 1 is a reflection load of WPF types, 2 compare files with Windows line endings). None of them failed because of these changes (they failed identically before the first commit). Run `dotnet test src/WinVelo.Tests/WinVelo.Tests.csproj` on Windows, and the UI tests (`src/WinVelo.UiTests`).
3. **Manual checks on a Windows VM** (not possible here): the startup elevation flow (accept and decline UAC), `--test-mode` without a prompt, Apps page status/cancel, Tools cleanup Stop button, restore-point creation, and the Arabic layout of the new dialogs.
4. **WhatsApp Store id `9NKSQGP7F2NH`**: format checked, not confirmed against the Store (no access). Run `winget show --id 9NKSQGP7F2NH --source msstore`.
5. **Chocolatey package names** (all 36) have a valid format but could not be checked: community.chocolatey.org was unreachable. Run `choco search <id> --exact` for each, or check the website.
6. **Tweak source links**: learn.microsoft.com was unreachable, so links were checked for format only. Run `dotnet run --project tools/SourceChecker/SourceChecker.csproj`.
7. **winget exit codes** used in `PackageCommands` (`0x8A150014`, `0x8A15002B`, `0x8A150061`) are taken from winget's documented return codes from memory; confirm them against the current [returnCodes.md](https://github.com/microsoft/winget-cli/blob/master/doc/windows/package-manager/winget/returnCodes.md).
8. **Code signing**: apply to [SignPath Foundation](https://signpath.org), add the secrets and variables listed in `docs/SIGNING.md`, set `WINVELO_SIGNING=signpath`, and check the SignPath action's current inputs before the first signed release.
9. **Decisions**: A-24, A-27, A-28, A-38 above.
10. **Release**: after merging, tag `v1.0.2`; the workflow refuses a tag that does not match `Directory.Build.props`.

## 5. Suggestions for the next version

1. Shared-target model for tweaks (A-25): reference-count registry values and services that several tweaks touch, so "already active" tweaks get journal records and every undo is exact.
2. Detect over-the-shoulder elevation (A-26) and warn before applying HKCU tweaks.
3. Make the external Catalog folder opt-in (`--catalog <path>`) and show its origin on each tweak card.
4. Run the UI tests and a VM integration run (`tests/vm`) in CI on a self-hosted Windows runner.
5. Code signing through SignPath Foundation.
6. Report service failures honestly (A-27) with a distinct "Not available on this edition" status instead of success.
7. Merge the 21 overlapping tweaks into one owner per value, or show "also changed by …" in the preview.
8. Make the DNS benchmark cancellable and run all Tools operations through `OperationRunner` with progress.
9. Remove dead code (`ScriptRunner.RunCmdAsync`, `apps.winget.json`) and the duplicated CatalogValidator/test logic.
10. Take the major package updates (JsonSchema.Net 9, xunit runner 4, Test SDK 18, WPF-UI 4.3) through Dependabot with a UI check.

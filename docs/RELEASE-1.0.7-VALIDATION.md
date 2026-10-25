# WinVelo 1.0.7 validation — 2026-10-25

## Defects addressed

- DNS verification previously required IPv6 addresses even when the adapter's IPv6 binding was disabled. This produced a false Google DNS failure after IPv4 DNS had already changed. Enabled protocol families now determine capture, application, verification and rollback.
- DNS failures now preserve native error details. Address rollback is verified, failed rollback records remain recoverable, and DoH changes target enabled protocol families only.
- Starting the CLI without arguments in a console now opens an interactive prompt. Unknown or conflicting options return a readable error; dry-run takes precedence over execution flags for tweaks, profiles and tools.
- Startup filtering no longer clears and recreates every unchanged row after a toggle. Accessible checkbox toggles execute the associated command. Simulation no longer scans host Startup folders, and desktop.ini is excluded from application entries.

## Completed local checks

- Release unit/integration suite: **467 passed, 0 failed, 0 skipped**. Includes native process timeout/context tests, DNS protocol/DoH script checks, rollback/error cases, CLI argument/dry-run cases and Startup accessibility/container preservation.
- CLI subprocess matrix: **56 passed**. Covers help/list/info, invalid arguments, tweak/preset apply, feature enable/disable, all cleaner categories, nine DNS providers, DoH, configuration sections, health, repairs, updates, diagnostics, detection, recovery preview and interactive persistence. Mutating scenarios use simulation. The CLI self-test completed **54 apply/undo round trips** against fake registry services.
- Native Google DNS regression: preconditions required the active adapter to already use static 8.8.8.8 / 8.8.4.4 with IPv6 disabled. Reapplying those same settings returned success; readback confirmed unchanged IPv4 addresses and a still-disabled IPv6 binding. No alternate DNS provider or protocol binding was applied to the host.
- The DNS/DoH/DHCP GUI scenario passed in simulation. After fixing row preservation and accessible command invocation, the targeted Startup toggle/refresh/navigation GUI regression passed.
- Release solution build with warnings treated as errors: **0 warnings, 0 errors**.
- Catalog validator: **142 tweaks, 6 presets, 242 apps, 0 errors**.

## Desktop isolation

The final broad local GUI run was interrupted at the user's request because visible automation disturbed desktop work. It is **not** reported as a successful full GUI run. Earlier physical-input attempts were also sensitive to foreground ownership; they are not counted as successful final checks.

Visible tests now require `WINVELO_ALLOW_DESKTOP_UI_TESTS=1`. Both test discovery and the app-launch helper enforce this opt-in. The default guard was checked locally: all 12 runtime GUI cases were skipped and no app window was launched. The isolated GitHub Windows UI job explicitly enables them and uploads its TRX results. Its outcome must be checked independently; configuration alone does not establish a passing remote run.

For unattended local verification, run:

```powershell
dotnet build WinVelo.sln -c Release -p:TreatWarningsAsErrors=true
dotnet test src/WinVelo.Tests/WinVelo.Tests.csproj -c Release --no-build
```

DNS mutation scripts are additionally exercised by a real Windows PowerShell process with mutation cmdlets replaced by asserted function stubs. These tests check IPv4-only and dual-stack branches, DHCP and DoH without changing network settings.

## Limits

Most modifying scenarios use fakes rather than a full real-mode Windows VM matrix. The native Google regression covers the reported IPv4-only case; it does not prove all adapters, VPNs, policies, Windows builds or DoH implementations behave identically. This release is not described as universally error-free. Local packages are unsigned and are not GitHub Actions provenance attestations.

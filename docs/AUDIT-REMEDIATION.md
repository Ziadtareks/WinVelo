# Audit remediation — 2026-10-22

Scope: tweaks, tools and repairs first. Win11 Creator / ISO modification is deferred. This is a draft implementation awaiting disposable Windows VM validation.

## Findings addressed in code

| Finding | Area | Implementation |
|---|---|---|
| F01 | Simulation deletion | Cleaner and file actions return before real deletion. |
| F02 | Script recovery | Persisted undo plans are executed by session/crash recovery. |
| F03 | Failed rollback | Failed records remain active and retryable; records from other sessions are not marked reverted. |
| F04 | Native service failures | sc/net exit, timeout and process-start failures are checked; changed service state is read back. |
| F05 | Composite detection | A registry marker cannot verify a script; probes are used where available and the UI rescans actual state. |
| F06 | Feature UI binding | Bound the actual feature collection and added selection/status notifications. |
| F07 | Script-only disable | Added explicit inverse/capture plans; unsupported disable requests fail without claiming success. |
| F08 | GUI/CLI JSON | Both use WinVeloConfig with stable serialized names and case-insensitive import. |
| F09 | Configuration sections | Preflight resolves apps, features, DNS, update mode and staged apply/undo; unknown IDs fail before execution. |
| F10 | BitLocker | Marked irreversible; no re-encryption or recovery-key restoration is claimed. |
| F11 | Snapshot restore | Default registry values, typed data and service state are restored; partial failures are reported. |
| F12 | Task failures | Denied operations fail; deleted task XML is captured before mutation and can be recreated. |
| F13 | DNS | Catalog providers, IPv6, selected adapters, explicit DoH, baseline history, readback and actual UDP DNS query timing. |
| F14 | Toolbox history | Startup, services, update policy, network settings, DNS, power plans, features and OpenSSH journal changes. Individual/session undo includes module IDs; session preview respects selected operations. |
| F15 | Native PowerShell results | Cancellation/start failures affect Success; native results are checked in built-in repair/catalog scripts; accepted restart codes are normalized after explicit validation. |
| F16 | CI count drift | Smoke workflow derives tweak count from the catalog. |
| F17 | Parity checks | Pinned source manifest runs on fresh clones/CI and checks app identities and curated aliases. |
| F18 | Importer overwrite | All source JSON is parsed first; a fresh source/review proposal is produced outside Catalog. No executable stubs or automatic catalog replacement. |
| F19 | Tool navigation | OpenSSH, AutoLogon and classic panels are reachable; service controls and Appx management added. |
| F20 | svchost | Separate high-risk needs-review tweak with captured original threshold; removed from essential Services. |
| F21 | Power plans | Stable owned GUID prevents duplicates; only the managed plan is removed; prior plan captured. |
| F22 | Plan relationships | Conflicts, duplicates, dependency order, missing dependencies and cycles checked before writes; GUI uses a single engine batch. |

## Additional changes

- 242 apps cover all 239 upstream winget identities while preserving the original curated keys, including Node.js LTS. Current Node.js has a separate key.
- 34 Appx entries support explicit current-user removal and Store links; app data removal has no automatic rollback.
- Registry export uses full hive names, default-value syntax and correct QWORD byte order, and explicitly identifies omitted non-registry actions.
- Real-mode memory reporting no longer invents a minimum amount of memory saved.

## Validation completed

- Release solution build: zero warnings/errors, warnings treated as errors.
- 429 unit/integration tests passed, including all eight original audit reproductions covered by regression checks.
- 3 UI smoke tests passed: Tools, History and accessibility.
- 4 CLI simulation smoke cases passed, including a configuration with apps, features, DNS/DoH and update mode.
- CatalogValidator: 142 tweaks, 6 presets, 242 apps, zero errors. PresetGenerator and CatalogStats ran.
- No vulnerable NuGet packages reported for current configured sources.
- Importer successfully staged all six source configs and 68 review entries without rewriting Catalog.
- All system-changing execution was simulated; no real tweaks were applied to the development PC.

## Remaining validation and coverage

See [UPSTREAM.md](UPSTREAM.md) for the pinned source, exact mappings and differences. 62 of the 68 upstream tweak/control keys map to catalog settings, four map to dedicated tools, and two existing exclusions remain: Adobe hosts blocking and the malformed upstream Settings Home toggle. CTT-specific PowerShell profiles remain excluded. No claim of full behavioral parity is made.

Before release, validate apply/undo on disposable Windows 10/11 VMs, including service permissions, scheduled-task recreation, DNS/IPv6/DoH, NFS/OpenSSH, feature restart states and failure recovery. Legacy script undo can restore catalog defaults rather than the original state when no capture routine exists. Appx/cache removal and disk decryption are not fully reversible. The shared operation gate serializes mutations within the application process; it is not a cross-process transaction lock.

# WinUtil source coverage

## Audited source

- Repository: [ChrisTitusTech/winutil](https://github.com/ChrisTitusTech/winutil), MIT (CT Tech Group LLC).
- Source commit: `2f0bdc8fca52e1ebe31a8d98216428061d3f8956` (October 21, 2026).
- Machine-readable mapping: [Catalog/SourceTracking/parity.json](../Catalog/SourceTracking/parity.json). All 68 upstream tweak/control keys have an explicit disposition. A mapped entry means a local equivalent exists; it does **not** certify identical behavior or Windows VM verification.

## Runtime coverage

| Area | Source entries | WinVelo implementation |
|---|---:|---|
| Tweaks and tweak controls | 68 | 62 keys mapped to catalog tweaks, 4 mapped to dedicated tools, 2 explicit gaps below |
| Features and configuration controls | 33 | Optional features, classic control panels, OpenSSH, AutoLogon and registry backup are accessible through Tools. CTT profile install/remove remain excluded. |
| DNS providers | 8 | All 8 are loaded from the catalog, plus DHCP. Adapter selection, IPv4/IPv6, optional DoH, actual DNS-query benchmark. Automatic fastest-provider selection includes only unfiltered providers explicitly marked benchmark eligible. |
| Appx packages | 34 | Individual selection, current-user detection/removal and Store links. App data is not backed up; no automatic undo. |
| Applications | 239 | Curated app catalog expanded to 242 entries by upstream winget identity, preserving existing local keys. Package-manager identifiers remain separate. |
| Presets | 4 source groups | 6 locally curated presets; generator excludes high-risk, irreversible and needs-review actions. |

The local catalog has **142 tweaks**; this is not a claim of 142 upstream tweaks. Win11 Creator / ISO modification is outside this implementation scope at the user's request.

## Explicit parity gaps

- `WPFTweaksBlockAdobeNet`: existing exclusion retained. No Adobe domain/hosts block list is installed. This is an outstanding parity gap, not an implemented action.
- `WPFToggleHideSettingsHome`: existing exclusion retained. The upstream `show:home` registry value is not implemented. An actual Settings page visibility feature needs a separately reviewed implementation.
- `WPFWinUtilInstallPSProfile` and `WPFWinUtilUninstallPSProfile`: CTT-specific personal PowerShell profiles are not installed or removed.

## Dedicated controls and differences

- `WPFchangedns`: Tools → DNS. Changes one selected adapter. DoH is explicit and requires a supported Windows build.
- `WPFAddUltPerf`: Tools → Power Plan. Creates/activates WinVelo's stable plan GUID `266bcd4c-fb39-4d09-bcda-8fa0dc0bd6a3` only when absent.
- `WPFRemoveUltPerf`: removes only WinVelo's managed plan, switching to Balanced first when needed. Other power plans are not removed. The previous active plan is journaled.
- `WPFOOSUbutton`: opens the official O&O Software page. No third-party binary is downloaded or executed automatically.
- `WPFTweaksSvcHostSplitting`: standalone high-risk, needs-review tweak. Removed from the essential Services script; original registry threshold captured before applying. Excluded from presets; pending VM verification.
- `WPFToggleMouseAcceleration`, `WPFToggleTaskbarAlignment`, `WPFToggleTaskView`: mapped to explicit local choices in the parity manifest.
- `WPFTweaksDisableBitLocker`: marked irreversible. A journal cannot recreate disk encryption or recover discarded recovery keys. Upstream default undo is not presented as re-encryption.
- Servicing protection still prevents weakening Windows Update, Defender and Store infrastructure. Upstream scripts are curated and can differ for that reason.

## Importing updates safely

```powershell
dotnet run --project tools/UpstreamImporter -- C:\source\winutil\config C:\review\new-proposal
```

The output directory must not exist. Every required JSON input is parsed before files are written. Literal control characters within upstream JSON strings are normalized for parsing. Importer output is a **proposal**; it never overwrites Catalog. Review action semantics, aliases, localization, sources and rollback plans, then promote selected edits manually. It writes normalized source files and a review manifest with SHA-256 hashes. It generates no executable catalog entries or fallback scripts.

Run CatalogValidator, PresetGenerator, CatalogStats and the test suite after promotion. The checked-in parity manifest is exercised on every clone and in CI; it does not require an ignored upstream checkout. Updating that manifest requires auditing the new source commit.

## Validation limits

Build, catalog checks and simulation tests validate code paths, serialized rollback plans and command construction. They do not replace disposable Windows 10/11 VM testing of DNS/DoH, service permissions, NFS, feature restart behavior, scheduled tasks or destructive app removal. No system tweaks were applied to the development computer during this remediation.

Registry/service/task baselines and supported module changes are journaled. Legacy scripts without a capture routine use their catalog undo plan, which may restore defaults rather than the exact prior state. Cleaner/Appx data removal and disk decryption cannot be fully undone. History keeps failed undo records retryable instead of marking them restored.

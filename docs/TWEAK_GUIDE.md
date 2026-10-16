# Tweak Authoring Guide

Every tweak in WinVelo is data-driven, defined as a JSON document in the `Catalog/Tweaks/` directory, and strictly validated against `Catalog/schema/tweak.schema.json`.

---

## 1. Principles for New Tweaks

1. **Official Documentation Source Required**:
   - Every tweak must provide a valid Microsoft Learn URL, Windows IT Pro documentation link, or hardware vendor whitepaper in the `source` field.
   - Tweaks based on internet hearsay or unverified forum claims are rejected.
2. **Reversibility**:
   - Every tweak must include complete `apply` and `undo` action sets.
   - The engine automatically records the true baseline in the write-ahead Rollback Journal, but the `undo` block acts as a static fallback.
3. **Edition Accuracy**:
   - Specify `windows.minBuild`, `windows.maxBuild`, and supported `windows.editions` (e.g. `Home`, `Pro`, `Enterprise`, `Education`).
   - If a tweak only takes effect on Enterprise/Education (e.g., Telemetry Level 0), explain this clearly in `effectNote`.
4. **Reliable Detection & Engine Honesty**:
   - Detection evaluates all verifiable `apply` actions (registry keys, system services, scheduled tasks).
   - A tweak reports `Applied` only if **all** of its verifiable actions are satisfied.
   - If only some verifiable actions are configured, the engine reports `Partial` (tri-state indeterminate toggle, `PARTIAL ⚠` badge), never falsely claiming full application.
   - Tweaks with pure PowerShell actions and no inspectable state report `Unknown` (`CAN'T VERIFY`).
   - Use `detect.any` **only** to define mutually exclusive alternatives for the same setting (e.g. GPO policy key vs user/system policy key for `AllowTelemetry`, or build-specific keys like `DisableSearchBoxSuggestions` vs `BingSearchEnabled` in `pref.bingsearch`).
   - `detect.any` must never be used to short-circuit stateful verification of composite tweaks where multiple distinct settings must all be applied.
5. **No Dangerous Operations**:
   - Do not delete critical system files, disable Windows Defender, or break core networking dependencies.

6. **Preset Role (`presetRole`)**:
   - Every tweak declares how presets may use it. Only `optimization` tweaks can be added to a generated preset.
   - `preference` = personal taste (dark mode, taskbar alignment, seconds in the clock). `fix` = only for a specific problem (MPO flicker, sleep issues, dual-boot clock). `action` = one-time operation (restore point, disk cleanup). `advanced` = manual tuning that should be read before applying.
   - Presets are built by `tools/PresetGenerator` from topic tags: Safe/Low go into Standard, Medium only into Full, and High, irreversible, Experimental or needsReview tweaks are always excluded with a reason shown to the user.
   - If two tweaks change the same registry value differently, the generator keeps the first one alphabetically and lists the other as a conflict. After changing tags, risk or `presetRole`, run the generator and commit the updated `Catalog/Presets/*.json`.

7. **Arabic Text**:
   - Every tweak and preset needs an Arabic `name` (and ideally `description`, `whatItDoes`, `sideEffects`) in `Catalog/Localization/ar.json`. `LocalizationTests` fails if one is missing.
   - Keep registry value names, product names and numbers in Latin script.
   - New UI text: XAML uses `{loc:Tr Key}` with the English text in `src/Tweakora.App/Localization/Lang-en.json`; view models use `LocalizationManager.T("English text")`. Add the Arabic text to `Lang-ar.json`.

---

## 2. Risk Rating Rubric

Every tweak must be assigned an honest, calibrated risk level based on the following rubric:

- **Safe**:
  - Per-user (`HKCU`) UI preference, instantly reversible, no feature lost.
  - Examples: show file extensions, dark mode, classic context menu, left-align taskbar.
  - *Floor rule*: Safe tweaks MUST be strictly per-user (`HKCU`). Any tweak targeting machine configuration (`HKLM`) cannot be Safe.

- **Low**:
  - Machine policy (`HKLM`) or benign system preference, reversible, no feature a typical user relies on is lost.
  - Examples: telemetry level reduction, diagnostic data limits, verbose logon, long path support, detailed BSOD.

- **Medium**:
  - Turns off a Windows feature some users rely on (such as hibernation, Widgets, Copilot, background apps, location services, lock screen content, Bing search integration), or requires a system reboot (`requiresReboot: true`).
  - Examples: disabling hibernation, disabling Widgets button, disabling Copilot policy, disabling background app permissions.

- **High**:
  - Modifies Windows services (`Service` action), security configurations (BitLocker, UAC, SmartScreen, firewall), Windows Update policies/actions, Microsoft Store, core networking protocol stack (IPv6, Teredo, DNS, Nagle), or is irreversible (`reversible: false`).
  - *Floor rules enforced by validator*:
    - `reversible === false` $\implies$ **High** (always).
    - Modifies or disables protected Windows servicing components or policies $\implies$ **High** (always).
    - Presets cannot contain High-risk tweaks.

---

## 3. Example Tweak Definition

```json
[
  {
    "schemaVersion": "1.0.0",
    "id": "ui.explorer.classic-context-menu",
    "name": "Enable Windows 10 Classic Context Menu",
    "description": "Restores the classic right-click context menu in Windows 11 File Explorer without requiring 'Show more options'.",
    "category": "UI",
    "subcategory": "Explorer",
    "risk": "Safe",
    "impacts": ["Ui"],
    "windows": {
      "minBuild": 22000,
      "editions": ["Home", "Pro", "Enterprise", "Education"]
    },
    "requiresReboot": false,
    "requiresExplorerRestart": true,
    "detect": {
      "type": "Registry",
      "hive": "HKCU",
      "key": "Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32",
      "valueName": "",
      "matchType": "Exists"
    },
    "apply": [
      {
        "type": "Registry",
        "hive": "HKCU",
        "key": "Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32",
        "operation": "Set",
        "valueName": "",
        "valueKind": "String",
        "data": ""
      }
    ],
    "undo": [
      {
        "type": "Registry",
        "hive": "HKCU",
        "key": "Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}",
        "operation": "DeleteKey"
      }
    ],
    "source": "https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers",
    "verifiedOnBuild": "26100"
  }
]
```

---

## 4. How to Validate Your Changes

Before submitting a Pull Request, always execute the automated catalog validator:

```powershell
# From the repository root
dotnet run --project tools/CatalogValidator/CatalogValidator.csproj -- Catalog/
```

Then run the full automated test suite:

```powershell
dotnet test Tweakora.sln
```

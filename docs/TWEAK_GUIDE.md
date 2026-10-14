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

---

## 2. Example Tweak Definition

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

## 3. How to Validate Your Changes

Before submitting a Pull Request, always execute the automated catalog validator:

```powershell
# From the repository root
dotnet run --project tools/CatalogValidator/CatalogValidator.csproj -- Catalog/
```

Then run the full automated test suite:

```powershell
dotnet test Tweakora.sln
```

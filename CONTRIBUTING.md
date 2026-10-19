# Contributing to WinVelo

Thank you for your interest in contributing to **WinVelo**! WinVelo is an open-source project committed to **absolute honesty, transparency, and safety** in Windows configuration.

---

## Code of Conduct & Guiding Principles

1. **Honesty Over Hype**: We never accept placebo tweaks, registry voodoo, or exaggerated claims (e.g. *"boosts FPS by 20%"*, *"eliminates input lag"*, *"speeds up internet by 200%"*).
2. **Documented Evidence**: Every tweak must cite official Microsoft documentation (Microsoft Learn, Group Policy CSPs, MSDN) or reference an exact, verifiable location in the Windows Settings app.
3. **Reversibility by Default**: Every tweak must have a verified `undo` action that restores the system to its baseline. If a tweak is inherently irreversible (such as uninstallation of an inbox package), it must be explicitly flagged with `"reversible": false` and carry an appropriate risk rating (`Medium` or `High`).
4. **Safety & Servicing Protection**: Tweaks must not corrupt Windows Component-Based Servicing (CBS), disable essential security safeguards without warnings, or prevent subsequent Windows Cumulative Updates.

---

## How to Add a Tweak to the Catalog

The tweak catalog is stored under `Catalog/Tweaks/*.json` (`essential.json`, `preferences.json`, `advanced.json`, `extras.json`, `performance.json`).

### 1. Schema Fields Reference

All tweaks must conform strictly to `Catalog/schema/tweak.schema.json`:

```json
{
  "schemaVersion": "1.0.0",
  "id": "ui.taskbar.show-seconds-clock",
  "name": "Show Seconds in Taskbar Clock",
  "description": "Displays seconds alongside hours and minutes in the Windows taskbar system clock.",
  "category": "ui",
  "subcategory": "taskbar",
  "risk": "Safe",
  "impacts": ["ui"],
  "tags": ["taskbar", "clock"],
  "presets": ["recommended"],
  "windows": {
    "minBuild": 22621
  },
  "requiresReboot": false,
  "requiresExplorerRestart": true,
  "detect": {
    "type": "Registry",
    "hive": "HKCU",
    "key": "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",
    "valueName": "ShowSecondsInSystemClock",
    "matchType": "Equals",
    "expectedValue": 1
  },
  "apply": [
    {
      "type": "Registry",
      "hive": "HKCU",
      "key": "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",
      "operation": "Set",
      "valueName": "ShowSecondsInSystemClock",
      "valueKind": "DWord",
      "data": 1
    }
  ],
  "undo": [
    {
      "type": "Registry",
      "hive": "HKCU",
      "key": "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",
      "operation": "Set",
      "valueName": "ShowSecondsInSystemClock",
      "valueKind": "DWord",
      "data": 0
    }
  ],
  "evidence": "ui-equivalent",
  "whatItDoes": "Configures the Windows Shell to render seconds in the notification area digital clock.",
  "sideEffects": "Slightly increases CPU wakeups (negligible on modern CPUs).",
  "uiEquivalentLocation": "Settings › Personalization › Taskbar › Taskbar behaviors › Show seconds in system tray clock",
  "source": "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-taskbar",
  "sources": [
    "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-taskbar"
  ],
  "sourceQuote": "This policy setting allows users to display seconds in the system tray clock.",
  "needsReview": false
}
```

### 2. Evidence Classification Rules

Every tweak must be tagged with one of three evidence tiers:

- **`documented`**: Backed by formal Microsoft Learn documentation, Group Policy CSP definitions, or MSDN API specs. Must include `sources` and a verified `sourceQuote`.
- **`ui-equivalent`**: Ordinary Windows settings available in Windows Settings (`Win + I`) or Control Panel. Must include `uiEquivalentLocation` with the exact breadcrumb navigation path (e.g., `Settings › Personalization › Colors`).
- **`situational`**: Reserved exclusively for tweaks that solve a specific documented technical problem, hardware workaround, or driver defect (e.g. S0 sleep network disconnects, multiplane overlay stutter). Must include `whatItDoes` and `sideEffects`.

### 3. Risk Rubric

- **`Safe`**: Modifies user preferences (`HKCU`), standard UI visual settings, or benign options. Zero risk of system instability. Reversible instantly.
- **`Low`**: Machine-wide policies (`HKLM`) or benign service configurations. Reversible.
- **`Medium`**: Modifies network protocols, telemetry policies, background services, or application behaviors. Enforces the Unified Preview Dialog.
- **`High`**: Destructive or irreversible changes (e.g., package removals, BitLocker disable). Strictly prohibited on physical workstations.

---

## Local Verification & Quality Bar

Before submitting a Pull Request, verify that your changes pass all checks:

1. **Run the Catalog Validator**:
   ```powershell
   dotnet run --project tools/CatalogValidator/CatalogValidator.csproj
   ```
   *Must exit with `0 error(s)`.*

2. **Run All Unit Tests**:
   ```powershell
   dotnet test src/WinVelo.Tests/WinVelo.Tests.csproj -c Debug
   ```
   *All tests must pass.*

3. **Run UI Tests in Test Mode**:
   ```powershell
   dotnet test src/WinVelo.UiTests/WinVelo.UiTests.csproj -c Debug
   ```
   *All UI automation tests must pass in `--test-mode`.*

4. **Code Quality**:
   - 0 compiler warnings.
   - Follow standard C# code conventions.
   - Do not include personal paths or temporary debugging files in commits.

# Upstream WinUtil Porting & Tracking 🔄

## Upstream Repository Information
- **Repository**: [ChrisTitusTech/winutil](https://github.com/ChrisTitusTech/winutil)
- **License**: MIT License (CT Tech Group LLC)
- **Latest audited commit**: `7514faf0d0f4bff9b6c5d9213426ab52520cfac4` (October 19, 2026)
- **Initial port commit**: `07ccd8e2e755a706f31569808b31f5b77acad6a9` (October 13, 2026)

---

## Porting Summary & Statistics

| Source File | Total Items | Ported Count | Not Ported Count | Notes |
|---|---|---|---|---|
| `config/tweaks.json` | 67 | **65** | **2** | 61 of 63 actionable tweaks plus all 4 UI button controls. Excluded: Adobe URL block list and the no-op Settings Home setting. |
| `config/feature.json` | 33 | **31** | **2** | 2 CTT-specific PowerShell profile items excluded. |
| `config/preset.json` | 4 | **4** | **0** | Standard, Minimal, Advanced, AppxDefault. |
| `config/dns.json` | 8 | **8** | **0** | 100% ported (IPv4 and IPv6). |
| `config/appx.json` | 34 | **34** | **0** | 100% ported. |
| `config/applications.json` | 236 | **36** | **200** | Curated app catalog. This is intentionally not a 1:1 port. |
| **Total** | **382** | **178** | **204** | Core tweaks, features, presets, DNS, and Appx: **142 of 146 (97.3%)**. Apps are curated separately. |

---

## Items Not Ported & Exact Reasons

The following items from WinUtil were intentionally excluded based on safety, legal, and architectural guidelines:

### 1. `WPFTweaksBlockAdobeNet` ("Adobe URL Block List - Enable")
- **Source**: `config/tweaks.json`
- **Upstream Action**: Injects hundreds of domain overrides into `hosts` to block Adobe activation and licensing telemetry.
- **Reason Not Ported**: Can be used to bypass software licensing and violate third-party Terms of Service. Excluded per user instruction.

### 2. `WPFToggleHideSettingsHome` ("Settings Home Page")
- **Source**: `config/tweaks.json`
- **Upstream Action**: Writes `SettingsPageVisibility` with a `show:home` value.
- **Reason Not Ported**: `SettingsPageVisibility` only accepts the `showonly:` and `hide:` prefixes, so the value has no defined effect. Removed in WinVelo 1.0.1 as a no-effect tweak.

### 3. `WPFWinUtilInstallPSProfile` ("CTT PowerShell Profile - Install")
- **Source**: `config/feature.json`
- **Upstream Action**: Downloads and executes a custom PowerShell 7+ profile containing personal alias definitions, third-party module imports, and Chris Titus Tech branding.
- **Reason Not Ported**: Vendor-specific branding and personalized shell customization; not appropriate for WinVelo core system toolbox.

### 4. `WPFWinUtilUninstallPSProfile` ("CTT PowerShell Profile - Remove")
- **Source**: `config/feature.json`
- **Upstream Action**: Deletes the CTT PowerShell profile.
- **Reason Not Ported**: Paired with the install script above; not applicable to WinVelo.

---

## Dedicated UI Controls

Upstream implements four `config/tweaks.json` entries as button handlers rather than ordinary registry tweaks. WinVelo provides native equivalents in the **Tools** tab:

- `WPFchangedns` -> **DNS & Network** tool with provider benchmarks and adapter switching.
- `WPFAddUltPerf` -> **Power Plan** tool that adds and activates the hidden Ultimate Performance plan.
- `WPFRemoveUltPerf` -> Switching back to **Balanced** restores the Windows default plan. WinVelo does not delete power schemes, so this is not a destructive "remove" action.
- `WPFOOSUbutton` -> Button that opens the official O&O Software page. WinVelo never downloads or runs third-party binaries automatically.

The former standalone Storage Sense disable tweak is now ported as `performance.system.storage-sense-disable`, with a mutual `oppositeOf` relationship to the Storage Sense enable tweak.

---

## Safety Modifications to Upstream Content

1. **`WPFTweaksDisableBitLocker` ("BitLocker - Disable")**:
   - **Upstream**: Listed under `Essential Tweaks`.
   - **WinVelo**: Moved to `Advanced Tweaks - CAUTION`. Risk level set to **High**. Red warning text added: *"Removes disk encryption and exposes data to unauthorized physical access."* Excluded from all presets.
2. **`WPFOOSUbutton` ("O&O ShutUp10++ - Run")**:
   - **Upstream**: Downloads third-party executable and executes it automatically.
   - **WinVelo**: Opens the official O&O Software download page in the default web browser. No automatic binary downloading.
3. **`WPFTweaksServices` ("Services - Set to Manual")**:
   - Explicitly breaks down all target services (`CscService`, `DiagTrack`, `MapsBroker`, `StorSvc`, `SharedAccess`).
   - Explains why `CscService`, `DiagTrack`, and `SharedAccess` are set to `Disabled`.
4. **High Risk Guarding**:
   - `WPFTweaksRemoveEdge`, `WPFTweaksRemoveOneDrive`, `WPFTweaksWindowsAI`, `WPFTweaksDisableIPv6`:
   - Risk set to **High**, with explicit warning dialogs and comprehensive simulated undo testing.
5. **WinVelo Extras**:
   - Verified tweaks developed in WinVelo that are not present in WinUtil are preserved in a dedicated "WinVelo Extras" section.

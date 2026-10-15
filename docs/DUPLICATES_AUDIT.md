# Catalog Overlap and Deduplication Audit

## Executive Summary

- **Initial Tweak Count:** 149 tweaks
- **Duplicates Pruned:** 15 exact duplicate definitions removed
- **Final Canonical Tweak Count:** 134 tweaks
- **Exact Duplicates Remaining:** 0 (enforced via `CatalogValidator` in CI)
- **Partial Overlaps:** 10 target collisions documented, linked via `relatedTweaks`, and displayed in the UI details drawer as *"Overlaps with: X"*.
- **Backward Compatibility:** Preserved via `aliases` on canonical definitions; old journals, presets, and CLI commands referencing removed IDs or WinUtil origin keys resolve transparently.

---

## 1. True Duplicates Resolved

True duplicates occur when multiple catalog entries apply identical registry settings or service states with identical data. In each case, one canonical tweak was retained (favoring clearer user-facing names, comprehensive documentation, explicit side effects, and verified evidence), and redundant entries were removed. Their IDs and upstream origin keys were attached to the canonical tweak's `aliases` array.

| Canonical Tweak ID | Canonical Name | Removed Duplicate IDs | Aliases Attached | Rationale |
| :--- | :--- | :--- | :--- | :--- |
| `gaming.input.disable-mouse-acceleration` | Disable Pointer Acceleration (Enhance Pointer Precision) | `performance.input.raw-mouse`, `pref.mouseacceleration` | `["performance.input.raw-mouse", "pref.mouseacceleration", "WPFToggleMouseAcceleration"]` | Rich explanation of 1:1 pointer mapping, verified UI equivalent in Control Panel Mouse Properties. |
| `privacy.activity.activity-history` | Disable Activity History Publishing | `essential.activity` | `["essential.activity", "WPFTweaksActivity"]` | Specific policy targeting CloudContent / PublishUserActivities; avoids ambiguous "Activity" shorthand. |
| `privacy.telemetry.diagnostic-data` | Set Diagnostic Data to Required (Basic) Only | `essential.telemetry` | `["essential.telemetry", "WPFTweaksTelemetry"]` | Accurate and compliant description for modern Windows 10/11 telemetry levels (`AllowTelemetry` = 0/1). |
| `ui.explorer.classic-context-menu` | Enable Windows 10 Classic Context Menu | `advanced.rightclickmenu` | `["advanced.rightclickmenu", "WPFTweaksRightClickMenu"]` | Explicitly targets Explorer CLSID inproc server with documented reversibility and clear impact. |
| `ui.taskbar.end-task-right-click` | Enable End Task Option on Taskbar | `essential.endtaskontaskbar` | `["essential.endtaskontaskbar", "WPFTweaksEndTaskOnTaskbar"]` | Clearer naming matching modern Windows 11 Developer settings. |
| `ui.explorer.show-file-extensions` | Always Show Known File Extensions | `pref.showext` | `["pref.showext", "WPFToggleShowExt"]` | Full documentation with UI equivalent location (`Folder Options › View`). |
| `ui.explorer.show-hidden-files` | Show Hidden Files, Folders, and Drives | `pref.hiddenfiles` | `["pref.hiddenfiles", "WPFToggleHiddenFiles"]` | Standard Windows Explorer naming with complete revert logic. |
| `ui.system.enable-long-paths` | Enable Win32 Long Paths (>260 Characters) | `pref.longpaths` | `["pref.longpaths", "WPFToggleLongPaths"]` | Documented FileSystem policy change with Microsoft documentation link. |
| `ui.taskbar.alignment-left` | Set Taskbar Alignment to Left | `pref.taskbaralignment` | `["pref.taskbaralignment", "WPFToggleTaskbarAlignment"]` | Clearer description; avoids generic "pref" prefix. |
| `ui.taskbar.disable-task-view-button` | Hide Task View Button from Taskbar | `pref.taskview` | `["pref.taskview", "WPFToggleTaskview"]` | Accurate UI label matching Windows Settings. |
| `performance.network.delivery-optimization-simple` | Disable Delivery Optimization Peer-to-Peer (HTTP Only) | `essential.deliveryoptimization` | `["essential.deliveryoptimization", "WPFTweaksDeliveryOptimization"]` | Fully documented bandwidth impact and Group Policy references. |
| `performance.system.storage-sense` | Enable Storage Sense Auto-Cleanup | `advanced.storage` | `["advanced.storage", "WPFTweaksStorage"]` | Comprehensive Storage Sense policy documentation with clear side effects. |
| `ai.search.disable-bing-web-search` | Disable Bing Cloud Search in Start Menu | `pref.bingsearch` | `["pref.bingsearch", "WPFToggleBingSearch"]` | Specific scope addressing web query leakage from the local Start Menu. |
| `gaming.mode.enable` | Enable Windows Game Mode | `pref.gamemode` | `["pref.gamemode", "WPFToggleGameMode"]` | Aligns with Microsoft Game Mode specification and gaming impacts. |

---

## 2. Partial Overlaps Identified and Handled

Partial overlaps occur when tweaks configure different aspects of a shared Windows component, service, or registry subkey, or where one tweak is a broader profile/bundle and the other is a fine-grained toggle. These entries are preserved, bidirectionally linked via `relatedTweaks`, and flagged in the UI.

| Shared Target (Registry / Service) | Overlapping Tweaks | Nature of Overlap | Handled via `relatedTweaks` |
| :--- | :--- | :--- | :--- |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Search\SearchboxTaskbarMode` | `advanced.display`, `pref.taskbarsearch` | `advanced.display` sets best performance defaults (including compacting search); `pref.taskbarsearch` lets users select search icon mode. | Added `pref.taskbarsearch` to `advanced.display.relatedTweaks` and vice-versa. |
| `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\DisabledComponents` | `advanced.disableipv6`, `advanced.ipv46`, `advanced.teredo`, `network.teredo.disable-tunneling` | Different bitmasks on the same IPv6 components flag (`0xFF` for complete disable, `0x20` for IPv4 preference, `0x08` for Teredo disable). | Linked bidirectionally across networking entries. |
| `HKCU\Control Panel\Desktop\WindowMetrics\MinAnimate` | `advanced.display`, `performance.visual.disable-animations` | `advanced.display` is a comprehensive visual performance bundle; `performance.visual.disable-animations` is an isolated toggle for window animations. | Linked bidirectionally. |
| `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}\SensorPermissionState` | `essential.location`, `privacy.location.disable-sensor` | `essential.location` disables location platform + services machine-wide; `privacy.location.disable-sensor` isolates sensor permission state. | Linked bidirectionally. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarMn` | `advanced.display`, `ui.taskbar.disable-chat-button` | `advanced.display` hides unneeded taskbar icons as part of performance mode; `ui.taskbar.disable-chat-button` is an isolated Teams toggle. | Linked bidirectionally. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\LaunchTo` | `advanced.removehomeandgallery`, `ui.explorer.open-to-this-pc` | `advanced.removehomeandgallery` removes Home/Gallery namespace nodes; `ui.explorer.open-to-this-pc` configures startup folder target (`1` for This PC). | Linked bidirectionally. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\ShowTaskViewButton` | `advanced.display`, `ui.taskbar.disable-task-view-button` | `advanced.display` strips taskbar extras; `ui.taskbar.disable-task-view-button` is a dedicated toggle. | Linked bidirectionally. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy\01` | `essential.deletetempfiles`, `performance.system.storage-sense` | `essential.deletetempfiles` executes immediate temporary folder purging via PowerShell; `performance.system.storage-sense` schedules background automatic cleanup. | Linked bidirectionally. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDa` | `essential.widget`, `ui.taskbar.disable-widgets-button` | `essential.widget` removes widget packages and sets policy; `ui.taskbar.disable-widgets-button` simply hides the taskbar icon. | Linked bidirectionally. |
| `HKLM\SOFTWARE\Microsoft\Windows\Dwm\OverlayTestMode` | `performance.gpu.disable-mpo`, `pref.multiplaneoverlay` | `performance.gpu.disable-mpo` applies modern DWM OverlayTestMode=5; `pref.multiplaneoverlay` sets legacy GraphicsDrivers DisableOverlays flag. | Linked bidirectionally. |

---

## 3. Presets & CI Validator

- **Presets**: All files in `Catalog/Presets/*.json` (`preset.gaming.json`, `preset.privacy.json`, `preset.recommended.json`) reference canonical tweak IDs.
- **CI Validator Rule**: `ValidateNoExactDuplicates` was integrated into `tools/CatalogValidator/Program.cs`. It computes a deterministic target signature across registry keys, values, services, and script actions for every tweak. Any collision with identical actions triggers a build/CI error.
- **Test Coverage**:
  - `Tweakora.Tests` suite passes (207/207 passed).
  - Alias resolution verified in CLI (`--info`, `--tweaks`), `DashboardViewModel`, `HistoryViewModel`, and `SmartAdvisorService`.

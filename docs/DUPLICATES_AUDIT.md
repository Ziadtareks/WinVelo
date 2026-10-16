# Catalog Overlap and Deduplication Audit

## Executive Summary

- **Initial Tweak Count:** 149 tweaks
- **Exact Duplicates Pruned (Kept as Aliases):** 8 canonical tweaks with aliases locked in `Catalog/aliases.lock.json`
- **Redundant Factory-Default Opposites Removed:** 4 tweaks (`pref.mouseacceleration`, `advanced.storage`, `pref.taskbaralignment`, `pref.taskview`) removed without aliases (registered in `DeprecatedTweaks` with clear `use Undo on <canonical id>` errors)
- **Distinct Overlapping Tweaks Restored:** 3 tweaks (`essential.telemetry`, `essential.activity`, `pref.bingsearch`) restored from pre-merge baseline and linked via bidirectional `relatedTweaks`
- **Final Canonical Tweak Count:** **137 tweaks** (149 - 8 - 4 = 137)
- **Exact Duplicates Remaining:** 0 (enforced via `ValidateNoExactDuplicates` in CI)
- **Alias Integrity:** Locked via `Catalog/aliases.lock.json` and verified in CI via `ValidateAliasesLock`
- **Conflict Prevention:** Registry value collisions with conflicting data must be explicitly declared via `oppositeOf: "<id>"` (enforced via `ValidateNoConflictingRegistryData` in CI)

---

## 1. Per-Pair Classification & Resolution Matrix (15 Reviewed Entries)

During initial deduplication in commit `23a4ae7`, 15 tweak entries were removed under the assumption that all were exact duplicates. A rigorous audit against their canonical counterparts revealed three distinct categories:

### A. True Exact Duplicates (8 Retained as Aliases)
These entries perform byte-for-byte identical registry and system actions as their canonical counterparts. The redundant entry was removed and attached as an alias in `Catalog/aliases.lock.json`.

| Canonical ID | Canonical Name | Removed Duplicate ID(s) | Aliases Attached | Rationale |
| :--- | :--- | :--- | :--- | :--- |
| `gaming.input.disable-mouse-acceleration` | Disable Pointer Acceleration (Enhance Pointer Precision) | `performance.input.raw-mouse` | `["performance.input.raw-mouse"]` | Exact match on `MouseSpeed=0`, `MouseThreshold1=0`, `MouseThreshold2=0`. Rich documentation preserved. |
| `gaming.mode.enable` | Enable Windows Game Mode | `pref.gamemode` | `["pref.gamemode", "WPFToggleGameMode"]` | Exact match on `AllowAutoGameMode=1`, `AutoGameModeEnabled=1`. Aligns with Game Mode spec. |
| `performance.network.delivery-optimization-simple` | Disable Delivery Optimization Peer-to-Peer (HTTP Only) | `essential.deliveryoptimization` | `["essential.deliveryoptimization", "WPFTweaksDeliveryOptimization"]` | Exact match on `DODownloadMode=0`. Fully documented Group Policy references. |
| `ui.explorer.classic-context-menu` | Enable Windows 10 Classic Context Menu | `advanced.rightclickmenu` | `["advanced.rightclickmenu", "WPFTweaksRightClickMenu"]` | Exact match on Explorer CLSID inproc server override. |
| `ui.explorer.show-file-extensions` | Always Show Known File Extensions | `pref.showext` | `["pref.showext", "WPFToggleShowExt"]` | Exact match on `HideFileExt=0`. Full UI equivalent documentation. |
| `ui.explorer.show-hidden-files` | Show Hidden Files, Folders, and Drives | `pref.hiddenfiles` | `["pref.hiddenfiles", "WPFToggleHiddenFiles"]` | Exact match on `Hidden=1`. Standard Explorer setting. |
| `ui.taskbar.end-task-right-click` | Enable End Task Option on Taskbar | `essential.endtaskontaskbar` | `["essential.endtaskontaskbar", "WPFTweaksEndTaskOnTaskbar"]` | Exact match on `TaskbarEndTask=1`. Matches Windows 11 Developer settings. |
| `ui.system.enable-long-paths` | Enable Win32 Long Paths (>260 Characters) | `pref.longpaths` | `["pref.longpaths", "WPFToggleLongPaths"]` | Exact match on `LongPathsEnabled=1`. Documented FileSystem policy. |

### B. Opposite Actions Setting Factory Defaults (4 Removed Without Aliases)
These entries merely reapplied factory Windows defaults. In WinVelo's architecture, applying a tweak stores pre-change state in the rollback journal, and the `Undo` command restores the factory baseline. Re-applying the default as a standalone tweak is redundant with `Undo`. **They must never be aliased to the opposite setting.** They were removed without aliases, and lookups return clear deprecation errors.

| Removed Tweak ID | Removed Action (Default Re-applied) | Canonical Opposite ID | Canonical Action | Resolution & Deprecation Error |
| :--- | :--- | :--- | :--- | :--- |
| `pref.mouseacceleration` | Sets `MouseSpeed/Threshold1/Threshold2 = 1/6/10` (Acceleration ON) | `gaming.input.disable-mouse-acceleration` | Sets `0/0/0` (Acceleration OFF) | Removed without alias. Lookups return: `Tweak 'pref.mouseacceleration' was removed: use Undo on 'gaming.input.disable-mouse-acceleration' instead.` |
| `advanced.storage` | Sets `StoragePolicy\01 = 0` (Storage Sense OFF) | `performance.system.storage-sense` | Sets `01 = 1` (Storage Sense ON) | Removed without alias. Lookups return: `Tweak 'advanced.storage' was removed: use Undo on 'performance.system.storage-sense' instead.` |
| `pref.taskbaralignment` | Sets `TaskbarAl = 1` (Taskbar Centered) | `ui.taskbar.alignment-left` | Sets `TaskbarAl = 0` (Taskbar Left) | Removed without alias. Lookups return: `Tweak 'pref.taskbaralignment' was removed: use Undo on 'ui.taskbar.alignment-left' instead.` |
| `pref.taskview` | Sets `ShowTaskViewButton = 1` (Task View Visible) | `ui.taskbar.disable-task-view-button` | Sets `ShowTaskViewButton = 0` (Task View Hidden) | Removed without alias. Lookups return: `Tweak 'pref.taskview' was removed: use Undo on 'ui.taskbar.disable-task-view-button' instead.` |

### C. Distinct / Broader Tweaks (3 Restored & Linked via Overlaps)
These entries were falsely flagged as duplicates despite configuring broader policies, additional registry keys, or distinct values. They were restored to the catalog and linked to related entries via `relatedTweaks` and `oppositeOf`.

| Restored Tweak ID | Catalog File | What it Configures | Overlapping ID | Why Distinct (Not a Duplicate) | Handled via |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `essential.telemetry` | `essential.json` | 13 registry keys across privacy, advertising, speech, inking, SIUF rules, plus DiagTrack/WerSvc service disables | `privacy.telemetry.diagnostic-data` | Target only configures `AllowTelemetry=0`. `essential.telemetry` is a comprehensive debloat bundle. | Bidirectional `relatedTweaks` link. |
| `essential.activity` | `essential.json` | Sets `EnableActivityFeed = 1`, `PublishUserActivities = 0`, `UploadUserActivities = 0` | `privacy.activity.activity-history` | Target sets `EnableActivityFeed = 0`. Different policy behavior. | Bidirectional `relatedTweaks` and `"oppositeOf": "privacy.activity.activity-history"`. |
| `pref.bingsearch` | `preferences.json` | Sets `DisableSearchBoxSuggestions = 1` AND `BingSearchEnabled = 0` | `ai.search.disable-bing-web-search` | Target only sets `DisableSearchBoxSuggestions = 1`. Lacks `BingSearchEnabled`. | Bidirectional `relatedTweaks` link. |

---

## 2. Partial Overlaps Identified and Handled

Partial overlaps occur when tweaks configure different aspects of a shared component or subkey, or when one tweak is a broader profile and the other is a fine-grained toggle. These entries are preserved, bidirectionally linked via `relatedTweaks`, and flagged in the UI details drawer. Where conflicting data is intentionally set, `oppositeOf` is declared.

| Shared Target (Registry / Service) | Overlapping Tweaks | Nature of Overlap | Handled via |
| :--- | :--- | :--- | :--- |
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\System\EnableActivityFeed` | `essential.activity` (sets 1), `privacy.activity.activity-history` (sets 0) | Differing policy states on activity feed synchronization. | Bidirectional `relatedTweaks` and `oppositeOf: "privacy.activity.activity-history"`. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Search\SearchboxTaskbarMode` | `advanced.display` (sets 0), `pref.taskbarsearch` (sets 1) | `advanced.display` minimizes taskbar; `pref.taskbarsearch` sets search icon mode. | Bidirectional `relatedTweaks` and `oppositeOf: "advanced.display"`. |
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization\DODownloadMode` | `performance.network.delivery-optimization-simple` (0), `network.optimization.limit-peering-lan` (1) | HTTP only vs local LAN peering. | Bidirectional `relatedTweaks` and `oppositeOf: "performance.network.delivery-optimization-simple"`. |
| `HKLM\SOFTWARE\Microsoft\Windows\Dwm\OverlayTestMode` | `performance.gpu.disable-mpo` (5), `pref.multiplaneoverlay` (1) | Modern DWM OverlayTestMode=5 vs legacy GraphicsDrivers DisableOverlays flag. | Bidirectional `relatedTweaks` and `oppositeOf: "performance.gpu.disable-mpo"`. |
| `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\DisabledComponents` | `advanced.disableipv6` (255), `advanced.ipv46` (32), `advanced.teredo` (1), `network.teredo.disable-tunneling` (8) | Different bitmasks on the IPv6 component flag. | Linked bidirectionally via `relatedTweaks` and annotated with `oppositeOf`. |
| `HKCU\Control Panel\Desktop\WindowMetrics\MinAnimate` | `advanced.display`, `performance.visual.disable-animations` | Visual performance bundle vs isolated window animations toggle. | Linked bidirectionally via `relatedTweaks`. |
| `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\...` | `essential.location`, `privacy.location.disable-sensor` | Machine-wide location platform disable vs isolated sensor permission state. | Linked bidirectionally via `relatedTweaks`. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarMn` | `advanced.display`, `ui.taskbar.disable-chat-button` | Performance strip-down vs dedicated Teams chat toggle. | Linked bidirectionally via `relatedTweaks`. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\LaunchTo` | `advanced.removehomeandgallery`, `ui.explorer.open-to-this-pc` | Namespace node removal vs startup folder target. | Linked bidirectionally via `relatedTweaks`. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\ShowTaskViewButton` | `advanced.display`, `ui.taskbar.disable-task-view-button` | Performance strip-down vs dedicated Task View toggle. | Linked bidirectionally via `relatedTweaks`. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy\01` | `essential.deletetempfiles`, `performance.system.storage-sense` | Immediate PowerShell purge vs scheduled background auto-cleanup. | Linked bidirectionally via `relatedTweaks`. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDa` | `essential.widget`, `ui.taskbar.disable-widgets-button` | Package debloat vs taskbar icon hide. | Linked bidirectionally via `relatedTweaks`. |

---

## 3. CI Validator & Guardrails

Four layers of automated CI checks in `tools/CatalogValidator` prevent accidental regressions:

1. **`ValidateNoExactDuplicates`**: Computes deterministic action signatures across all registry keys, values, services, and script actions. Fails if two distinct tweaks perform identical actions.
2. **`ValidateAliasesLock`**: Enforces that all aliases declared in the catalog exist in `Catalog/aliases.lock.json` and match the locked normalized apply signature and SHA-256 hash. Prevents aliasing opposite or non-identical actions.
3. **`ValidateNoConflictingRegistryData`**: Detects when two tweaks touch the same registry value with differing data. Fails the build unless at least one tweak explicitly declares `oppositeOf: "<id>"`.
4. **`ValidateRegistryKeys`**: Scans all registry actions across `detect`, `apply`, and `undo`. Rejects keys with empty path segments, leading/trailing backslashes, or consecutive backslashes (`\\`).

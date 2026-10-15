# WinVelo Host Manual Test Plan (Physical Machine)

> **SAFETY NOTICE & WARNING**
> This test plan is specifically authored for testing WinVelo on a physical Windows host.
> **DO NOT DEVIATE** from the specified tiers. All Medium risk, High risk, irreversible, and Experimental tweaks are explicitly cordoned off behind the **STOP LINE** at the end of this document and must **never** be executed on a primary daily workstation.

---

## Pre-Flight Checklist (Prerequisites)

Before launching WinVelo or running any test tiers, complete and verify every item below:

- [ ] **Full System Image Backup**: Create a full disk/system image using Macrium Reflect, Veeam Agent, or your preferred bare-metal backup software to an external drive.
- [ ] **Create System Restore Point**: Run `rstrui.exe` or use Windows System Properties to manually create a fresh System Restore point labeled `WinVelo-PreTest-Baseline`.
- [ ] **Verify Restore Point in rstrui.exe**: Launch `rstrui.exe`, select "Choose a different restore point", and confirm that `WinVelo-PreTest-Baseline` appears with today's date and time.
- [ ] **Close Background Applications**: Terminate games, code editors, office software, and non-essential system tray utilities to eliminate interference.
- [ ] **Verify Administrator Elevation**: Ensure WinVelo is launched with Administrator privileges.

---

## Tier 1: Safe HKCU Tweaks (10 Tests)

All tweaks in Tier 1 modify only user-level keys (`HKCU`) and have directly verifiable graphical UI reflections in Windows Settings or File Explorer.

---

### Test 1.1: Windows Dark Mode (`pref.darkmode`)
- **Action**: In WinVelo Tweaks tab, toggle `Windows Dark Mode` ON.
- **Expected Result**: System immediately switches to Dark Mode. An undo toast notification appears in the lower corner with an "Undo" button.
- **Where to Verify in Windows**:
  - Open **Windows Settings** (`Win + I`) › **Personalization** › **Colors**.
  - Verify **Choose your mode** is set to **Dark**.
- **Undo Action**: Click the "Undo" button on the in-app toast notification.
- **Verify Reversion**: System immediately switches back to Light mode; Settings confirms **Choose your mode** is **Light**.
- [ ] Passed

---

### Test 1.2: File Explorer File Name Extensions (`pref.showext`)
- **Action**: In WinVelo Tweaks tab, toggle `File Explorer - Show Known File Extensions` ON.
- **Expected Result**: File Explorer shows full extensions for all file types (e.g. `.txt`, `.png`).
- **Where to Verify in Windows**:
  - Open **File Explorer** (`Win + E`).
  - Click **View** › **Show** › check **File name extensions**. Verify it is checked and file extensions are visible in any folder.
- **Undo Action**: In WinVelo Tweaks tab, toggle `File Explorer - Show Known File Extensions` OFF.
- **Verify Reversion**: In File Explorer, file extensions are hidden again.
- [ ] Passed

---

### Test 1.3: File Explorer Hidden Files & Folders (`pref.hiddenfiles`)
- **Action**: In WinVelo Tweaks tab, toggle `File Explorer - Show Hidden Files` ON.
- **Expected Result**: Hidden files and folders (e.g. `AppData`) become visible with translucent icons.
- **Where to Verify in Windows**:
  - Open **File Explorer** (`Win + E`).
  - Click **View** › **Show** › verify **Hidden items** is checked.
- **Undo Action**: In WinVelo, toggle `File Explorer - Show Hidden Files` OFF.
- **Verify Reversion**: Hidden files and folders are hidden again; **Hidden items** is unchecked.
- [ ] Passed

---

### Test 1.4: Show Seconds in Taskbar Clock (`ui.taskbar.show-seconds-clock`)
- **Action**: In WinVelo Tweaks tab, toggle `Show Seconds in Taskbar Clock` ON.
- **Expected Result**: The system tray clock in the bottom right corner updates to display seconds (`HH:mm:ss`).
- **Where to Verify in Windows**:
  - Check the clock in the bottom right corner of the Windows Taskbar.
  - Open **Windows Settings** › **Personalization** › **Taskbar** › **Taskbar behaviors** › verify **Show seconds in system tray clock** is checked.
- **Undo Action**: In WinVelo, toggle `Show Seconds in Taskbar Clock` OFF.
- **Verify Reversion**: System tray clock returns to standard `HH:mm` display; setting is unchecked.
- [ ] Passed

---

### Test 1.5: Align Taskbar Icons to Left (`ui.taskbar.alignment-left`)
- **Action**: In WinVelo Tweaks tab, toggle `Align Taskbar Icons to the Left` ON.
- **Expected Result**: The Windows Start button and pinned apps instantly move to the left edge of the taskbar (Windows 10 style).
- **Where to Verify in Windows**:
  - Visually observe the taskbar layout.
  - Open **Windows Settings** › **Personalization** › **Taskbar** › **Taskbar behaviors** › verify **Taskbar alignment** is set to **Left**.
- **Undo Action**: In WinVelo, toggle `Align Taskbar Icons to the Left` OFF.
- **Verify Reversion**: Taskbar icons return to center alignment; **Taskbar alignment** displays **Center**.
- [ ] Passed

---

### Test 1.6: Disable Transparency Effects (`performance.visual.disable-transparency`)
- **Action**: In WinVelo Tweaks tab, toggle `Disable Transparency Effects` ON.
- **Expected Result**: Acrylic and Mica translucent blur effects on the taskbar, Start menu, and window borders turn solid opaque.
- **Where to Verify in Windows**:
  - Open **Windows Settings** › **Personalization** › **Colors**.
  - Verify **Transparency effects** toggle is **Off**.
- **Undo Action**: In WinVelo, toggle `Disable Transparency Effects` OFF.
- **Verify Reversion**: Open **Windows Settings** › **Personalization** › **Colors** › verify **Transparency effects** is **On**.
- [ ] Passed

---

### Test 1.7: Enable Windows Game Mode (`gaming.mode.enable`)
- **Action**: In WinVelo Tweaks tab, toggle `Enable Windows Game Mode` ON.
- **Expected Result**: Windows Game Mode scheduling is activated.
- **Where to Verify in Windows**:
  - Open **Windows Settings** › **Gaming** › **Game Mode**.
  - Verify the **Game Mode** switch is toggled to **On**.
- **Undo Action**: In WinVelo, toggle `Enable Windows Game Mode` OFF.
- **Verify Reversion**: Open **Windows Settings** › **Gaming** › **Game Mode** › verify the toggle is **Off**.
- [ ] Passed

---

### Test 1.8: Disable Pointer Acceleration (`pref.mouseacceleration`)
- **Action**: In WinVelo Tweaks tab, toggle `Disable Pointer Acceleration` ON.
- **Expected Result**: Hardware mouse movement becomes strictly linear 1:1 without dynamic OS curve acceleration.
- **Where to Verify in Windows**:
  - Press `Win + R`, type `main.cpl`, and press Enter.
  - Select the **Pointer Options** tab.
  - Verify **Enhance pointer precision** is **Unchecked**.
- **Undo Action**: In WinVelo, toggle `Disable Pointer Acceleration` OFF.
- **Verify Reversion**: Open `main.cpl` › **Pointer Options** › verify **Enhance pointer precision** is **Checked**.
- [ ] Passed

---

### Test 1.9: Disable Advertising ID (`privacy.advertising.advertising-id`)
- **Action**: In WinVelo Tweaks tab, toggle `Disable Advertising ID` ON.
- **Expected Result**: Apps are prevented from using the advertising ID for cross-app profiling.
- **Where to Verify in Windows**:
  - Open **Windows Settings** › **Privacy & security** › **General**.
  - Verify **Let apps show me personalized ads by using my advertising ID** is toggled to **Off**.
- **Undo Action**: In WinVelo, toggle `Disable Advertising ID` OFF.
- **Verify Reversion**: Verify the toggle returns to **On**.
- [ ] Passed

---

### Test 1.10: Enable 'End Task' in Taskbar Right-Click Menu (`essential.endtaskontaskbar`)
- **Action**: In WinVelo Tweaks tab, toggle `Enable 'End Task' on Taskbar` ON.
- **Expected Result**: Right-clicking any active window button on the taskbar presents a direct "End task" option.
- **Where to Verify in Windows**:
  - Open **Windows Settings** › **System** › **For developers**.
  - Verify **End Task** is toggled **On**.
  - Right-click any running application on the taskbar and verify "End task" appears in the context menu.
- **Undo Action**: In WinVelo, toggle `Enable 'End Task' on Taskbar` OFF.
- **Verify Reversion**: Settings toggle turns **Off**; "End task" disappears from taskbar context menus.
- [ ] Passed

---

## Tier 2: Low Risk HKLM Tweaks (5 Tests)

Tier 2 tweaks modify system-wide machine policies in `HKLM`. They are strictly reversible and verified by policy or service status.

---

### Test 2.1: Enable Win32 Long Paths (`ui.system.enable-long-paths`) *[Requires Explorer Restart]*
- **Action**: In WinVelo Tweaks tab, toggle `Enable Win32 Long Paths (>260 Chars)` ON.
- **Expected Result**:
  - WinVelo informs the user that an Explorer restart is required.
  - Value `LongPathsEnabled = 1` is written to `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem`.
- **Where to Verify in Windows**:
  - Open **Registry Editor** (`regedit.exe`) in read-only inspection mode:
  - Check `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem`.
  - Confirm `LongPathsEnabled` is `1`.
  - Restart Windows Explorer from Task Manager or accept WinVelo's prompt.
- **Undo Action**: In WinVelo, toggle `Enable Win32 Long Paths (>260 Chars)` OFF.
- **Verify Reversion**: In `regedit.exe`, confirm `LongPathsEnabled` is deleted or set to `0`.
- [ ] Passed

---

### Test 2.2: Hide Fast User Switching (`ui.logon.hide-fast-user-switching`)
- **Action**: In WinVelo Tweaks tab, toggle `Hide Fast User Switching Option` ON.
- **Expected Result**: Windows hides other user accounts from the Start Menu profile flyout.
- **Where to Verify in Windows**:
  - Open **Registry Editor** (`regedit.exe`):
  - Check `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System`.
  - Confirm `HideFastUserSwitching` is `1`.
- **Undo Action**: In WinVelo, toggle `Hide Fast User Switching Option` OFF.
- **Verify Reversion**: In `regedit.exe`, confirm `HideFastUserSwitching` is deleted or set to `0`.
- [ ] Passed

---

### Test 2.3: Disable Activity History (`essential.activity`)
- **Action**: In WinVelo Tweaks tab, toggle `Disable Activity History` ON.
- **Expected Result**: Windows stops recording local user application activities and timeline history.
- **Where to Verify in Windows**:
  - Open **Windows Settings** › **Privacy & security** › **Activity history**.
  - Verify **Store my activity history on this device** is unchecked or disabled by group policy.
- **Undo Action**: In WinVelo, toggle `Disable Activity History` OFF.
- **Verify Reversion**: Open **Windows Settings** › **Privacy & security** › **Activity history** › verify option can be checked again.
- [ ] Passed

---

### Test 2.4: Verbose BSoD Crash Parameters (`pref.detailedbsod`)
- **Action**: In WinVelo Tweaks tab, toggle `BSoD Verbose Mode` ON.
- **Expected Result**: Windows crash control is configured to show stop code parameters on bugcheck.
- **Where to Verify in Windows**:
  - Open `regedit.exe`:
  - Check `HKLM\SYSTEM\CurrentControlSet\Control\CrashControl`.
  - Confirm `DisplayParameters` is `1`.
- **Undo Action**: In WinVelo, toggle `BSoD Verbose Mode` OFF.
- **Verify Reversion**: Confirm `DisplayParameters` is `0` or deleted.
- [ ] Passed

---

### Test 2.5: Disable Windows Error Reporting (`privacy.reporting.windows-error-reporting`)
- **Action**: In WinVelo Tweaks tab, toggle `Disable Windows Error Reporting` ON.
- **Expected Result**: Automatic crash dump upload and error dialogue prompts are suppressed.
- **Where to Verify in Windows**:
  - Open `regedit.exe`:
  - Check `HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting`.
  - Confirm `Disabled` is `1`.
- **Undo Action**: In WinVelo, toggle `Disable Windows Error Reporting` OFF.
- **Verify Reversion**: Confirm `Disabled` is deleted or set to `0`.
- [ ] Passed

---

## Tier 3: Preset Workflow & Global Session Undo (History Tab)

This tier validates preset application through the preview modal, session tracking in the journal, and complete global rollback.

- [ ] **Step 3.1: Open Preset Preview**
  - On the **Home** tab, click **Apply Recommended Profile** on the Recommended Profile card.
  - Verify that the Unified Execution Dialog appears with a summary of changes, categorized by risk and category.
- [ ] **Step 3.2: Confirm & Apply Preset**
  - Click **Apply Changes** in the execution dialog.
  - Wait for execution to complete. Verify the progress indicator completes and the confirmation screen reports all actions succeeded.
- [ ] **Step 3.3: Verify Session in History Tab**
  - Navigate to the **History** tab.
  - Confirm that the top-most card represents the newly completed session (marked with `Preset (Recommended)` and current timestamp).
  - Verify the list of tweaks and action counts matches the preset contents.
- [ ] **Step 3.4: Execute Global Rollback**
  - Click **"Undo all WinVelo changes"** at the top of the History tab.
  - Confirm the confirmation warning dialog.
  - Verify all recorded journal sessions are processed and reverted newest-to-oldest.
- [ ] **Step 3.5: Verify Complete System Restoration**
  - Inspect the Tweaks tab: verify all tweaks touched by the preset have returned to their pre-application state badge.
  - Verify the History cards update their status badge to `Reverted`.

---

## Tier 4: Crash Recovery Mid-Batch

This tier validates that the write-ahead journal accurately captures in-flight operations and prompts for automatic rollback if WinVelo is forcibly terminated mid-apply.

- [ ] **Step 4.1: Queue a Multi-Tweak Batch**
  - In the Tweaks tab, select 4 Safe tweaks that are currently not applied.
  - Click **Review & apply** to open the Unified Execution Dialog.
- [ ] **Step 4.2: Simulate Application Crash**
  - Click **Apply Changes**.
  - While the progress bar is actively advancing, immediately kill `WinVelo.exe` via Task Manager (`End task`) or from an elevated command prompt: `taskkill /F /IM WinVelo.exe`.
- [ ] **Step 4.3: Relaunch WinVelo & Verify Recovery Dialog**
  - Launch `WinVelo.exe`.
  - Confirm that WinVelo detects the unfinished journal session and immediately displays the Crash Recovery Dialog: *"Unfinished Session Detected"*.
- [ ] **Step 4.4: Execute Crash Rollback**
  - Click **"Roll Back"** in the recovery dialog.
  - Confirm that WinVelo iterates through all actions logged prior to the crash and restores the original baseline values.
- [ ] **Step 4.5: Verify Clean Post-Recovery State**
  - Navigate to the History tab: confirm the interrupted session is marked as resolved/reverted.
  - Verify the affected tweaks in Windows Settings match their original baseline settings.

---

## Tier 5: CLI Safe Operations & Confirmation Guards

Validates that non-destructive command line options are strictly safe and that modifying operations enforce safety guards.

- [ ] **Step 5.1: CLI Tweak Inspection (`--info`)**
  - Open terminal and run: `winvelo-cli --info pref.darkmode`
  - Verify: Outputs whatItDoes, sideEffects, exactChanges, verified sources, and location in Windows Settings.
- [ ] **Step 5.2: CLI Dry-Run Preview (`--dry-run`)**
  - Run: `winvelo-cli --dry-run`
  - Verify: Analyzes all catalog tweaks and prints a simulation preview without touching any host registry keys or services.
- [ ] **Step 5.3: CLI Interactive Confirmation Guard**
  - Run: `winvelo-cli --tweak pref.darkmode --apply` without passing `-y` or `--yes`.
  - Verify: The CLI displays an interactive confirmation prompt: `Are you sure you want to apply this tweak? (y/N)`.
  - Press `N` or Enter: confirm execution aborts safely with code 0 and no changes occur.
- [ ] **Step 5.4: Generate Diagnostic Bundle (`--diagnostics`)**
  - Run: `winvelo-cli --diagnostics WinVelo-Diagnostics.zip`
  - Verify: Creates `WinVelo-Diagnostics.zip` containing `system_info.json`, `journal_summary.json`, `tweak_states.json`, and sanitized logs.
  - Open `WinVelo-Diagnostics.zip`: confirm username and user profile paths are sanitized to `<USER>`.

---

## ⛔ THE STOP LINE: Do NOT Test on a Physical Main Machine

The following 85 tweaks carry **Medium** risk, **High** risk, are **irreversible**, or are flagged **Experimental**. They must **NEVER** be applied on a primary physical workstation. Test them solely within dedicated disposable virtual machines.

| Tweak ID | Name | Risk | Reversible | Experimental |
| :--- | :--- | :---: | :---: | :---: |
| `advanced.bravedebloat` | Brave Browser - Debloat | Medium | Yes | No |
| `advanced.disablebgapps` | Background Apps Globally - Disable | Medium | Yes | No |
| `advanced.disablebitlocker` | BitLocker Drive Encryption - Disable | High | Yes | No |
| `advanced.disableipv6` | Disable IPv6 Support | Medium | Yes | No |
| `advanced.disablenotifications` | Action Center Notifications - Disable | Medium | Yes | No |
| `advanced.disablewarningforunsignedrdp` | Unsigned RDP Files Warning - Disable | Medium | Yes | No |
| `advanced.display` | Visual Effects - Set to Best Performance | Safe | Yes | No |
| `advanced.edgedebloat` | Edge Browser - Debloat | Medium | Yes | No |
| `advanced.ipv46` | IPv4 Over IPv6 - Prefer | Medium | Yes | No |
| `advanced.logiblock` | Logitech G-Hub Telemetry - Block | Medium | Yes | No |
| `advanced.razerblock` | Razer Synapse Telemetry - Block | Medium | Yes | No |
| `advanced.removeedge` | Edge Browser - Remove Completely | High | No | Yes |
| `advanced.removehomeandgallery` | File Explorer Home and Gallery - Disable | Safe | Yes | No |
| `advanced.removeonedrive` | Microsoft OneDrive - Uninstall | Medium | No | No |
| `advanced.reservedstorage` | Windows Reserved Storage - Disable | Medium | Yes | No |
| `advanced.rightclickmenu` | Right-Click Menu Previous Layout - Enable | Safe | Yes | No |
| `advanced.storage` | Storage Sense - Enable Automatic Run | Medium | Yes | No |
| `advanced.teredo` | Teredo Tunneling - Disable | Medium | Yes | No |
| `advanced.windowsai` | Windows AI Features - Disable | Medium | Yes | No |
| `ai.copilot.disable-policy` | Disable Windows Copilot Globally | Medium | Yes | No |
| `ai.copilot.disable-taskbar-button` | Hide Copilot Taskbar Button | Medium | Yes | No |
| `ai.recall.disable-snapshots` | Disable Windows Recall Snapshots | Medium | Yes | No |
| `ai.search.disable-bing-web-search` | Disable Bing Web Search in Start Menu | Medium | Yes | No |
| `essential.deletetempfiles` | Temporary Files - Remove | Low | No | No |
| `essential.deliveryoptimization` | Delivery Optimization - Disable | Medium | Yes | No |
| `essential.disableexplorerautodiscovery` | File Explorer Automatic Folder Discovery - Disable | Safe | Yes | No |
| `essential.disablestoresearch` | Microsoft Store Recommended Search Results - Disable | Medium | Yes | No |
| `essential.diskcleanup` | Disk Cleanup - Run | Low | No | No |
| `essential.hiber` | Hibernation - Disable | Medium | Yes | No |
| `essential.location` | Location Tracking - Disable | Medium | Yes | No |
| `essential.revertstartmenu` | Start Menu Previous Layout - Enable | Medium | Yes | No |
| `essential.services` | Services - Set to Manual | Medium | Yes | No |
| `essential.widget` | Widgets - Remove | Medium | Yes | No |
| `gaming.dvr.disable-background-recording` | Disable Background Game DVR Recording | Medium | Yes | No |
| `gaming.xbox.disable-game-bar` | Disable Xbox Game Bar Overlay | Medium | Yes | No |
| `network.dns.enable-doh` | Enable DNS over HTTPS (DoH) System Fallback | Medium | Yes | No |
| `network.optimization.limit-peering-lan` | Restrict Delivery Optimization to Local Network Only | Medium | Yes | No |
| `network.probing.disable-active-probing` | Disable NCSI Active Internet Probing | Medium | Yes | No |
| `network.teredo.disable-tunneling` | Disable Teredo IPv6 Tunneling | Medium | Yes | No |
| `network.wifi.disable-autoconnect-sense` | Disable Auto-Connect to Suggested Open Wi-Fi | Medium | Yes | No |
| `network.wifi.disable-hotspot-reporting` | Disable Wi-Fi Sense Hotspot Reporting | Medium | Yes | No |
| `performance.cpu.priority-separation` | Adjust Win32 Priority Separation | Medium | Yes | No |
| `performance.cpu.unpark-cores` | Disable CPU Core Parking | Medium | Yes | Yes |
| `performance.gaming.mmcss-responsiveness` | MMCSS Gaming Network Throttling | Medium | Yes | No |
| `performance.gpu.disable-mpo` | Disable Multi-Plane Overlay (MPO Workaround) | Medium | Yes | No |
| `performance.gpu.hags` | Hardware-Accelerated GPU Scheduling (HAGS) | Medium | Yes | No |
| `performance.input.buffer-queue` | Mouse & Keyboard Buffer Queue | Medium | Yes | Yes |
| `performance.memory.svchost-split-threshold` | Tune SvcHost Split Threshold | Medium | Yes | No |
| `performance.network.disable-remote-differential-compression` | Disable Remote Differential Compression API | Medium | Yes | No |
| `performance.ntfs.disable-8dot3-names` | Disable 8.3 Short File Name Creation | Medium | Yes | No |
| `performance.ntfs.disable-last-access` | Disable NTFS Last Access Time Updates | Medium | Yes | No |
| `performance.power.disable-throttling` | Disable Windows Power Throttling | Medium | Yes | No |
| `performance.search.prevent-indexing-on-battery` | Prevent Search Indexing While on Battery Power | Medium | Yes | No |
| `performance.system.disable-maintenance-wake` | Disable Automatic Maintenance Wake-Up Timers | Medium | Yes | No |
| `performance.system.storage-sense` | Enable Storage Sense Auto-Cleanup | Medium | Yes | No |
| `pref.batterypercentage` | Battery Percentage on Taskbar | Safe | Yes | No |
| `pref.bingsearch` | Start Menu Bing Search | Medium | Yes | No |
| `pref.disablelockscreen` | Lock Screen - Disable | Medium | Yes | No |
| `pref.hidesettingshome` | Hide Settings Home Page | Safe | Yes | No |
| `pref.loginblur` | Logon Screen Acrylic Blur | Medium | Yes | No |
| `pref.multiplaneoverlay` | Multiplane Overlay Workaround | Low | Yes | No |
| `pref.newoutlook` | Hide Try New Outlook Toggle | Safe | Yes | No |
| `pref.numlock` | Enable NumLock on Boot | Safe | Yes | No |
| `pref.s3sleep` | S3 Sleep | Medium | Yes | No |
| `pref.scrollbars` | Always Show Scrollbars | Safe | Yes | No |
| `pref.standbyfix` | S0 Sleep Network Connectivity | Medium | Yes | No |
| `pref.startmenurecommendations` | Start Menu Recommendations | Medium | Yes | No |
| `pref.stickykeys` | Disable Sticky Keys Shortcut | Safe | Yes | No |
| `pref.taskbaralignment` | Taskbar Centered Icons | Safe | Yes | No |
| `pref.taskbarsearch` | Taskbar Search Icon | Safe | Yes | No |
| `pref.taskview` | Taskbar Task View Icon | Medium | Yes | No |
| `pref.verboselogon` | Logon Verbose Mode | Low | Yes | No |
| `pref.windowsnapping` | Window Snap Assist Recommendations | Safe | Yes | No |
| `privacy.consent.deny-account-info` | Deny Apps Access to Account Information | Medium | Yes | No |
| `privacy.consent.deny-calendar` | Deny Apps Access to User Calendar & Appointments | Medium | Yes | No |
| `privacy.consent.deny-contacts` | Deny Apps Access to Contacts List | Medium | Yes | No |
| `privacy.consent.deny-diagnostics` | Deny Apps Access to Other Apps Diagnostic Info | Medium | Yes | No |
| `privacy.consent.deny-location` | Deny Apps Access to Location by Default | Medium | Yes | No |
| `privacy.consent.deny-microphone` | Deny UWP Apps Access to Microphone by Default | Medium | Yes | No |
| `privacy.consent.deny-notifications` | Deny UWP Apps Access to User Notifications | Medium | Yes | No |
| `privacy.consent.deny-webcam` | Deny UWP Apps Access to Webcam by Default | Medium | Yes | No |
| `privacy.location.disable-sensor` | Disable System Location Sensor | Medium | Yes | No |
| `privacy.spotlight.lockscreen-ads` | Disable Lock Screen Tips & Spotlight Ads | Medium | Yes | No |
| `privacy.suggestions.start-menu-promotions` | Disable Start Menu Suggested App Ads | Medium | Yes | No |
| `system.security.disable-autorun` | Disable AutoRun & AutoPlay on All Drives | Medium | Yes | No |
| `system.security.disable-llmnr` | Disable Link-Local Multicast Name Resolution (LLMNR) | Medium | Yes | No |
| `system.security.disable-lockscreen-camera` | Disable Camera Access on Lock Screen | Medium | Yes | No |
| `system.security.disable-rdp-auto-enable` | Disable Remote Desktop Inbound Connections | Medium | Yes | No |
| `system.security.disable-remote-assistance` | Disable Remote Assistance | Medium | Yes | No |
| `system.security.disable-wsh` | Disable Windows Script Host (VBS/JS Scripts) | Medium | Yes | No |
| `system.security.enforce-smartscreen` | Enforce Windows SmartScreen via Policy | Medium | Yes | No |
| `ui.explorer.classic-context-menu` | Restore Classic Right-Click Menu | Safe | Yes | No |
| `ui.explorer.hide-frequent-folders` | Hide Frequently Used Folders in Quick Access | Safe | Yes | No |
| `ui.explorer.hide-gallery` | Hide Gallery from File Explorer Navigation Pane | Safe | Yes | No |
| `ui.explorer.hide-recent-files` | Hide Recent Files in File Explorer Home | Safe | Yes | No |
| `ui.explorer.open-to-this-pc` | Open File Explorer to 'This PC' | Safe | Yes | No |
| `ui.explorer.show-file-extensions` | Always Show Known File Extensions | Safe | Yes | No |
| `ui.explorer.show-full-path-title` | Display Full Folder Path in File Explorer Title Bar | Safe | Yes | No |
| `ui.explorer.show-hidden-files` | Show Hidden Files and Folders | Safe | Yes | No |
| `ui.search.disable-search-highlights` | Disable Search Box Highlights & Illustrations | Safe | Yes | No |
| `ui.system.disable-tips-suggestions` | Disable Windows Welcome Experience and Suggestions | Safe | Yes | No |
| `ui.system.disable-welcome-experience` | Disable Windows Welcome Experience Screen | Safe | Yes | No |
| `ui.taskbar.disable-chat-button` | Hide Microsoft Teams Chat Icon on Taskbar | Medium | Yes | No |
| `ui.taskbar.disable-task-view-button` | Hide Task View Button from Taskbar | Medium | Yes | No |
| `ui.taskbar.disable-widgets-button` | Hide Widgets Weather Icon from Taskbar | Medium | Yes | No |
| `updates.activehours.enable-policy` | Enforce Active Hours Policy Against Mid-Day Restarts | Medium | Yes | No |
| `updates.au.notify-download` | Notify Before Downloading Windows Updates | Medium | Yes | No |
| `updates.drivers.exclude-from-updates` | Exclude Hardware Drivers from Windows Update | Medium | Yes | No |
| `updates.peering.metered-connections` | Block Delivery Optimization on Metered Connections | Medium | Yes | No |
| `updates.preview.disable-optional-content` | Disable Optional Preview Updates & Beta Rollouts | Medium | Yes | No |
| `updates.reboot.disable-auto-reboot-logged-on` | Prevent Forced Auto-Reboot While Logged On | Medium | Yes | No |

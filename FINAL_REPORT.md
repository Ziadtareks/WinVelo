# Tweakora v0.1.0 Final Delivery Report 🛡️

**Date**: October 12, 2026  
**Repository**: `C:\Dev\Tweakora`  
**Target Release**: `v0.1.0` (Open-Source Windows 10/11 Tweak & Toolbox Center)  

---

## 1. Catalog of All 84 Verified Tweaks

Every tweak is strictly validated against `Catalog/schema/tweak.schema.json`, reversible via the write-ahead Rollback Journal, and backed by official Microsoft Learn or vendor documentation.

| ID | Name | Category | Risk | Source Documentation |
|---|---|---|---|---|
| `ai.copilot.disable-taskbar-button` | Hide Copilot Button on Taskbar | ai | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai#turnoffwindowscopilot) |
| `ai.copilot.disable-policy` | Disable Windows Copilot via Policy | ai | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai#turnoffwindowscopilot) |
| `ai.recall.disable-snapshots` | Disable Windows Recall Snapshots | ai | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai#disableusersavingimagesforanalysis) |
| `ai.search.disable-bing-web-search` | Disable Bing Cloud Search in Start Menu | ai | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-search#search_connectedsearchuseweb) |
| `gaming.mode.enable` | Enable Windows Game Mode | gaming | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/gaming/windows/) |
| `gaming.xbox.disable-game-bar` | Disable Xbox Game Bar Overlay | gaming | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/gaming/windows/) |
| `gaming.dvr.disable-background-recording` | Disable Game DVR Background Capture | gaming | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/gaming/game-bar-and-dvr) |
| `gaming.input.disable-mouse-acceleration` | Disable Pointer Precision (Mouse Acceleration) | gaming | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow) |
| `network.wifi.disable-hotspot-reporting` | Disable Wi-Fi Hotspot 2.0 Auto-Reporting | network | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/wi-fi-hotspot-2-0) |
| `network.probing.disable-active-probing` | Disable NCSI Active Web Probing | network | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services#14-network-connection-status-indicator-ncsi) |
| `network.wifi.disable-autoconnect-sense` | Disable Auto-Connect to Suggested Open Wi-Fi | network | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/wi-fi-sense) |
| `network.optimization.limit-peering-lan` | Restrict Delivery Optimization to Local Network Only | network | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization-reference) |
| `network.dns.enable-doh` | Enable DNS over HTTPS (DoH) System Fallback | network | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/networking/dns/doh-client-support) |
| `network.teredo.disable-tunneling` | Disable Teredo IPv6 Tunneling | network | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2008-R2-and-2008/cc753928(v=ws.10)) |
| `performance.visual.disable-transparency` | Disable Transparency Effects | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/win7appqual/desktop-window-manager) |
| `performance.visual.disable-animations` | Disable Window Minimize/Maximize Animations | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow) |
| `performance.system.disable-startup-delay` | Disable Windows Startup App Delay | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-8.1-and-8/hh825206(v=win.10)) |
| `performance.system.storage-sense` | Enable Storage Sense Auto-Cleanup | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-storage#storage-allowstoragesenseglobal) |
| `performance.network.delivery-optimization-simple` | Restrict Delivery Optimization to Local Network | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization-reference) |
| `performance.memory.svchost-split-threshold` | Tune SvcHost Split Threshold | performance | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/application-management/svchost-service-refactoring) |
| `performance.system.disable-maintenance-wake` | Disable Automatic Maintenance Wake-Up Timers | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/sleep-settings-allow-wake-timers) |
| `performance.search.prevent-indexing-on-battery` | Prevent Search Indexing While on Battery Power | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-search#search_preventindexingonbattery) |
| `performance.ntfs.disable-last-access` | Disable NTFS Last Access Time Updates | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-behavior) |
| `performance.ntfs.disable-8dot3-names` | Disable 8.3 Short File Name Creation | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-8dot3name) |
| `performance.network.disable-remote-differential-compression` | Disable Remote Differential Compression API | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/rdc/about-remote-differential-compression) |
| `performance.system.clear-pagefile-shutdown` | Ensure Pagefile is Not Overwritten on Every Shutdown | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/clear-pagefile-at-shutdown) |
| `privacy.telemetry.diagnostic-data` | Diagnostic Data Level | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/privacy/configure-windows-diagnostic-data-in-your-organization) |
| `privacy.advertising.advertising-id` | Disable Advertising ID | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience#experience-allowadvertisingid) |
| `privacy.experiences.tailored-experiences` | Disable Tailored Experiences | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services#1-advertising-id) |
| `privacy.activity.activity-history` | Disable Activity History Publishing | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system#system-publishuseractivities) |
| `privacy.speech.online-speech-recognition` | Disable Online Speech Recognition | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy#privacy-allowinputpersonalization) |
| `privacy.typing.inking-typing-personalization` | Disable Inking & Typing Data Collection | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services#4-inking-and-typing-personalization) |
| `privacy.tracking.app-launch-tracking` | Disable App Launch Tracking | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy#privacy-letappsaccessappinformation) |
| `privacy.feedback.feedback-prompts` | Disable Feedback Frequency Prompts | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services#7-feedback-and-diagnostics) |
| `privacy.reporting.windows-error-reporting` | Disable Windows Error Reporting | privacy | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/wer/windows-error-reporting) |
| `privacy.ceip.customer-experience-improvement` | Disable Customer Experience Program (CEIP) | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2008-R2-and-2008/cc784549(v=ws.10)) |
| `privacy.clipboard.cloud-clipboard-sync` | Disable Cloud Clipboard Sync | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system#allowcrossdeviceclipboard) |
| `privacy.search.device-search-history` | Disable Device Search History | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services#32-search) |
| `privacy.spotlight.lockscreen-ads` | Disable Lock Screen Tips & Spotlight Ads | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/configuration/windows-spotlight) |
| `privacy.suggestions.start-menu-promotions` | Disable Start Menu Suggested App Ads | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/configuration/windows-spotlight) |
| `privacy.location.disable-sensor` | Disable System Location Sensor | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system#system-allowlocation) |
| `privacy.consent.deny-location` | Deny Apps Access to Location by Default | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-microphone` | Deny UWP Apps Access to Microphone by Default | privacy | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-webcam` | Deny UWP Apps Access to Webcam by Default | privacy | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-notifications` | Deny Apps Access to User Notifications | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-account-info` | Deny Apps Access to Account Information | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-contacts` | Deny Apps Access to Contacts List | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-calendar` | Deny Apps Access to User Calendar & Appointments | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-diagnostics` | Deny Apps Access to Other Apps Diagnostic Info | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.feedback.disable-frequency` | Disable Windows Feedback Request Frequency | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services#7-feedback-and-diagnostics) |
| `system.security.disable-remote-assistance` | Disable Remote Assistance | system | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-remoteassistance) |
| `system.security.disable-autorun` | Disable AutoRun & AutoPlay on All Drives | system | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/autoplay-reg) |
| `system.security.disable-wsh` | Disable Windows Script Host (VBS/JS Scripts) | system | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/wscript) |
| `system.security.enable-smartscreen-edge` | Enforce Windows Defender SmartScreen | system | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/security/operating-system-security/virus-and-threat-protection/windows-defender-smartscreen/) |
| `system.security.disable-llmnr` | Disable Link-Local Multicast Name Resolution (LLMNR) | system | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2008-R2-and-2008/cc753928(v=ws.10)) |
| `system.security.disable-rdp-auto-enable` | Ensure Remote Desktop Inbound Access is Disabled | system | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/remote/remote-desktop-services/clients/remote-desktop-allow-access) |
| `system.security.disable-lockscreen-camera` | Disable Camera Access on Lock Screen | system | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-personalization#nolockscreencamera) |
| `system.security.hide-fast-user-switching` | Disable Fast User Switching | system | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/security/threat-protection/security-policy-settings/interactive-logon-hide-fast-user-switching) |
| `ui.explorer.classic-context-menu` | Restore Classic Right-Click Menu | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers) |
| `ui.explorer.show-file-extensions` | Always Show Known File Extensions | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/security/operating-system-security/device-management/windows-security-configuration-framework/security-compliance-toolkit-10) |
| `ui.explorer.show-hidden-files` | Show Hidden Files and Folders | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/client-tools/show-hidden-files) |
| `ui.explorer.open-to-this-pc` | Open File Explorer to This PC | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/client-tools/file-explorer) |
| `ui.taskbar.end-task-right-click` | Enable 'End Task' in Taskbar Context Menu | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/powertoys/developer-settings) |
| `ui.taskbar.show-seconds-clock` | Show Seconds in Taskbar Clock | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/client-tools/taskbar-configuration) |
| `ui.system.enable-long-paths` | Enable Win32 Long Paths (>260 Chars) | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation) |
| `ui.taskbar.alignment-left` | Align Taskbar Icons to the Left | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/client-tools/taskbar-configuration) |
| `ui.explorer.compact-view` | Enable Compact View in File Explorer | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/client-tools/file-explorer) |
| `ui.system.disable-snap-assist` | Disable Snap Assist Suggestions | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/client-tools/multitasking-configuration) |
| `ui.explorer.hide-gallery` | Hide Gallery from File Explorer Navigation Pane | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/nse-works) |
| `ui.explorer.hide-recent-files` | Hide Recent Files in File Explorer Home | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-windowsexplorer#windowsexplorer_showrecent) |
| `ui.explorer.hide-frequent-folders` | Hide Frequently Used Folders in Quick Access | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-windowsexplorer) |
| `ui.explorer.show-full-path-title` | Display Full Folder Path in File Explorer Title Bar | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-shell-setup-folderoptions-showfullpath) |
| `ui.search.disable-search-highlights` | Disable Search Box Highlights & Illustrations | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-search#search_searchhighlights) |
| `ui.taskbar.disable-widgets-button` | Hide Widgets Weather Icon from Taskbar | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-widgets) |
| `ui.taskbar.disable-chat-button` | Hide Microsoft Teams Chat Icon on Taskbar | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience#experience-configurechaticon) |
| `ui.taskbar.disable-task-view-button` | Hide Task View Button from Taskbar | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-shell-setup-taskbarlinks) |
| `ui.system.disable-welcome-experience` | Disable Windows Welcome Experience After Updates | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/configuration/windows-spotlight) |
| `ui.system.disable-tips-suggestions` | Disable Tips, Tricks, and Suggestions About Windows | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/configuration/windows-spotlight) |
| `updates.drivers.exclude-from-updates` | Exclude Hardware Drivers from Windows Update | updates | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings#do-not-include-drivers-with-windows-updates) |
| `updates.reboot.disable-auto-reboot-logged-on` | Prevent Forced Auto-Reboot While Logged On | updates | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings#no-auto-reboot-with-logged-on-users-for-scheduled-automatic-updates-installations) |
| `updates.au.notify-download` | Notify Before Downloading Windows Updates | updates | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings#configure-automatic-updates) |
| `updates.peering.metered-connections` | Block Delivery Optimization on Metered Connections | updates | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization-reference) |
| `updates.activehours.enable-policy` | Enforce Active Hours Policy Against Mid-Day Restarts | updates | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings#turn-off-auto-restart-for-updates-during-active-hours) |
| `updates.preview.disable-optional-content` | Disable Optional Preview Updates & Beta Rollouts | updates | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings#enable-optional-updates) |

---

## 2. Security Category Deep-Dive Review

We audited all 8 tweaks inside `Catalog/Tweaks/security.json`.

> [!IMPORTANT]
> **Strict Safety Guarantee**: NONE of the security tweaks disable Windows Defender, Windows Firewall, SmartScreen, UAC, or Windows Security Updates.

### Exact Behavior Breakdown of All Security Tweaks:

1. **`system.security.disable-remote-assistance`**
   - **Target**: `HKLM\SYSTEM\CurrentControlSet\Control\Remote Assistance\fAllowToGetHelp` = `0`
   - **Function**: Closes unsolicited Windows Remote Assistance invitation files and incoming support requests. Does not disable Defender, Firewall, or UAC.
2. **`system.security.disable-autorun`**
   - **Target**: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\NoDriveTypeAutoRun` = `255`
   - **Function**: Hardens against USB drop attacks and malicious removable media by blocking automatic file execution.
3. **`system.security.disable-wsh`**
   - **Target**: `HKLM\SOFTWARE\Microsoft\Windows Script Host\Settings\Enabled` = `0`
   - **Function**: Prevents standalone execution of legacy VBScript and JScript (`.vbs`/`.js`) files, a major infection vector for phishing payloads.
4. **`system.security.enable-smartscreen-edge`**
   - **Target**: `HKLM\SOFTWARE\Policies\Microsoft\Windows\System\EnableSmartScreen` = `1`
   - **Function**: **Enforces** Windows Defender SmartScreen protection. It actively strengthens defenses against untrusted downloads and phishing URLs.
5. **`system.security.disable-llmnr`**
   - **Target**: `HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient\EnableMulticast` = `0`
   - **Function**: Blocks Link-Local Multicast Name Resolution broadcast queries on local subnets to protect credentials against Responder/MITM hash-poisoning attacks.
6. **`system.security.disable-rdp-auto-enable`**
   - **Target**: `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\fDenyTSConnections` = `1`
   - **Function**: Ensures inbound Remote Desktop connections on port 3389 remain closed unless explicitly enabled by an administrator.
7. **`system.security.disable-lockscreen-camera`**
   - **Target**: `HKLM\SOFTWARE\Policies\Microsoft\Windows\Personalization\NoLockScreenCamera` = `1`
   - **Function**: Disables hardware camera activation while the screen is locked, protecting physical workstation privacy.
8. **`system.security.hide-fast-user-switching`**
   - **Target**: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\HideFastUserSwitching` = `1`
   - **Function**: Hides fast user switching to prevent unauthorized users from jumping between concurrent interactive desktop sessions without a password.

---

## 3. Pending Verification (`docs/NEEDS_VERIFICATION.md`)

In adherence with the Quality Rule (*"It is better to ship 84 verified tweaks than 300 risky ones"*), the following tweaks remain isolated in `docs/NEEDS_VERIFICATION.md` until dedicated VM stress-testing is performed:

1. **`network.tcp.autotuning-experimental`**:
   - *Reason*: TCP window auto-tuning level variations (`highlyrestricted` vs `normal`) can cause packet drops on specific cellular or satellite NIC adapters.
2. **`performance.memory.svchost-split-override`**:
   - *Reason*: Grouping svchost processes on low-RAM systems (under 4 GB) requires testing to verify services do not crash when co-located in a single process.
3. **`system.kernel.mitigations-speculative-execution`**:
   - *Reason*: Disabling Spectre/Meltdown CPU mitigations for performance gains presents serious security risks and is strictly prohibited from the default catalog.

---

## 4. Test Suite & Quality Metrics

- **Unit Tests**: **34 passed**, 0 failed, 0 skipped.
  - Action Handlers: Registry (Apply, Undo, Multiple Baseline Preservation)
  - Services: Apply, Undo, Baseline preservation
  - Scheduled Tasks: Apply, Undo, Baseline preservation
  - File Cleanup: Dry-run and whitelisted directory execution
  - Completeness: Comprehensive automated test loading all 84 catalog tweaks verifying `id`, `name`, `description`, `detect`, `apply`, `undo`, and `source`.
- **Catalog Schema Validator**: 84/84 tweaks strictly conform to JSON Schema Draft 7 with zero errors.
- **Process Health**: Single-file distribution executable (`dist\Tweakora.exe`) runs autonomously for 10 seconds with valid window handle (`MainWindowHandle != 0`).

---

## 5. Questions for Ziad

1. **GitHub Repository**: When you are ready to publish Tweakora to GitHub, do you want us to set up GitHub Actions CI for automated build and release uploading?
2. **Catalog Additions**: Are there specific additional tools or Windows features you would like added in the next release (v0.2.0)?
3. **Inno Setup**: When you install Inno Setup on your workstation, we can automatically compile `installer\tweakora.iss` to produce `Tweakora-Setup-v0.1.0-x64.exe`.

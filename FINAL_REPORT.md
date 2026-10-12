# Tweakora v0.1.0 Final Delivery Report 🛡️

**Date**: October 12, 2026  
**Repository**: `C:\Dev\Tweakora`  
**Target Release**: `v0.1.0` (Open-Source Windows 10/11 Tweak & Toolbox Center)  

---

## 1. Catalog of All 79 Verified Tweaks

Every tweak is strictly validated against `Catalog/schema/tweak.schema.json`, reversible via the write-ahead Rollback Journal, and backed by official Microsoft Learn or vendor documentation with 100% verified HTTP 200 links.

| ID | Name | Category | Risk | Source Documentation |
|---|---|---|---|---|
| `ai.copilot.disable-taskbar-button` | Hide Copilot Taskbar Button | ai | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/manage-windows-copilot) |
| `ai.copilot.disable-policy` | Disable Windows Copilot Globally | ai | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/manage-windows-copilot) |
| `ai.recall.disable-snapshots` | Disable Windows Recall Snapshots | ai | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/manage-recall) |
| `ai.search.disable-bing-web-search` | Disable Bing Web Search in Start Menu | ai | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-search#disablesearchboxsuggestions) |
| `gaming.mode.enable` | Enable Windows Game Mode | gaming | Safe | [Microsoft Learn](https://support.microsoft.com/en-us/windows/use-game-mode-on-your-pc-5896ee1a-1e64-44b7-a3f2-892f33c37574) |
| `gaming.xbox.disable-game-bar` | Disable Xbox Game Bar Overlay | gaming | Safe | [Microsoft Learn](https://support.microsoft.com/en-us/windows/troubleshoot-xbox-game-bar-on-windows-574a4cf7-775b-4395-5c1a-ddc99d63645c) |
| `gaming.dvr.disable-background-recording` | Disable Background Game DVR Recording | gaming | Safe | [Microsoft Learn](https://support.microsoft.com/en-us/windows/adjust-capture-settings-for-xbox-game-bar-83be3e98-294b-4654-8e2b-f1f31f9b3ec7) |
| `gaming.input.disable-mouse-acceleration` | Disable Pointer Acceleration (Enhance Pointer Precision) | gaming | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfoa) |
| `network.wifi.disable-hotspot-reporting` | Disable Wi-Fi Sense Hotspot Reporting | network | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-wifi#allowwifihotspotreporting) |
| `network.probing.disable-active-probing` | Disable NCSI Active Internet Probing | network | Medium | [Microsoft Learn](https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/network-connectivity-status-indicator-active-probing-behavior) |
| `network.wifi.disable-autoconnect-sense` | Disable Auto-Connect to Suggested Open Wi-Fi | network | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-wifi) |
| `network.optimization.limit-peering-lan` | Restrict Delivery Optimization to Local Network Only | network | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization-reference) |
| `network.dns.enable-doh` | Enable DNS over HTTPS (DoH) System Fallback | network | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/networking/dns/doh-client-support) |
| `network.teredo.disable-tunneling` | Disable Teredo IPv6 Tunneling | network | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/configure-ipv6-in-windows) |
| `performance.visual.disable-transparency` | Disable Transparency Effects | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/win7appqual/desktop-window-manager) |
| `performance.visual.disable-animations` | Disable Window Minimize/Maximize Animations | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow) |
| `performance.system.storage-sense` | Enable Storage Sense Auto-Cleanup | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-storage#storage-allowstoragesenseglobal) |
| `performance.network.delivery-optimization-simple` | Disable Delivery Optimization Peer-to-Peer (HTTP Only) | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization-reference) |
| `performance.memory.svchost-split-threshold` | Tune SvcHost Split Threshold | performance | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/application-management/svchost-service-refactoring) |
| `performance.system.disable-maintenance-wake` | Disable Automatic Maintenance Wake-Up Timers | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/sleep-settings-allow-wake-timers) |
| `performance.search.prevent-indexing-on-battery` | Prevent Search Indexing While on Battery Power | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-search#search_preventindexingonbattery) |
| `performance.ntfs.disable-last-access` | Disable NTFS Last Access Time Updates | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-behavior) |
| `performance.ntfs.disable-8dot3-names` | Disable 8.3 Short File Name Creation | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-8dot3name) |
| `performance.network.disable-remote-differential-compression` | Disable Remote Differential Compression API | performance | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/rdc/about-remote-differential-compression) |
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
| `privacy.location.disable-sensor` | Disable System Location Sensor | privacy | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system#system-allowlocation) |
| `privacy.consent.deny-location` | Deny Apps Access to Location by Default | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-microphone` | Deny UWP Apps Access to Microphone by Default | privacy | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-webcam` | Deny UWP Apps Access to Webcam by Default | privacy | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-notifications` | Deny UWP Apps Access to User Notifications | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-account-info` | Deny Apps Access to Account Information | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-contacts` | Deny Apps Access to Contacts List | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-calendar` | Deny Apps Access to User Calendar & Appointments | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `privacy.consent.deny-diagnostics` | Deny Apps Access to Other Apps Diagnostic Info | privacy | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) |
| `system.security.disable-remote-assistance` | Disable Remote Assistance | security | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-remoteassistance) |
| `system.security.disable-autorun` | Disable AutoRun & AutoPlay on All Drives | security | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/autoplay-reg) |
| `system.security.disable-wsh` | Disable Windows Script Host (VBS/JS Scripts) | security | Medium | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/wscript) |
| `system.security.enforce-smartscreen` | Enforce Windows SmartScreen via Policy | security | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-smartscreen) |
| `system.security.disable-llmnr` | Disable Link-Local Multicast Name Resolution (LLMNR) | security | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2008-R2-and-2008/cc753928(v=ws.10)) |
| `system.security.disable-rdp-auto-enable` | Disable Remote Desktop Inbound Connections | security | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-server/remote/remote-desktop-services/clients/remote-desktop-allow-access) |
| `system.security.disable-lockscreen-camera` | Disable Camera Access on Lock Screen | security | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-devicelock#preventenablinglockscreencamera) |
| `ui.explorer.classic-context-menu` | Restore Classic Right-Click Menu | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers) |
| `ui.explorer.show-file-extensions` | Always Show Known File Extensions | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-shell-setup) |
| `ui.explorer.show-hidden-files` | Show Hidden Files and Folders | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-shell-setup) |
| `ui.explorer.open-to-this-pc` | Open File Explorer to This PC | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-shell-setup) |
| `ui.taskbar.end-task-right-click` | Enable 'End Task' in Taskbar Context Menu | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/get-started/enable-your-device-for-development) |
| `ui.taskbar.show-seconds-clock` | Show Seconds in Taskbar Clock | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience) |
| `ui.system.enable-long-paths` | Enable Win32 Long Paths (>260 Chars) | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation) |
| `ui.taskbar.alignment-left` | Align Taskbar Icons to the Left | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience#experience-taskbaralignment) |
| `ui.explorer.hide-gallery` | Hide Gallery from File Explorer Navigation Pane | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/shell/nse-works) |
| `ui.explorer.hide-recent-files` | Hide Recent Files in File Explorer Home | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-windowsexplorer#windowsexplorer_showrecent) |
| `ui.explorer.hide-frequent-folders` | Hide Frequently Used Folders in Quick Access | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-windowsexplorer) |
| `ui.explorer.show-full-path-title` | Display Full Folder Path in File Explorer Title Bar | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-shell-setup) |
| `ui.search.disable-search-highlights` | Disable Search Box Highlights & Illustrations | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-search#allowsearchhighlights) |
| `ui.taskbar.disable-widgets-button` | Hide Widgets Weather Icon from Taskbar | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-newsandinterests#allownewsandinterests) |
| `ui.taskbar.disable-chat-button` | Hide Microsoft Teams Chat Icon on Taskbar | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience#experience-configurechaticon) |
| `ui.taskbar.disable-task-view-button` | Hide Task View Button from Taskbar | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-shell-setup-taskbarlinks) |
| `ui.system.disable-welcome-experience` | Disable Windows Welcome Experience After Updates | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/configuration/windows-spotlight) |
| `ui.system.disable-tips-suggestions` | Disable Tips, Tricks, and Suggestions About Windows | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/configuration/windows-spotlight) |
| `ui.logon.hide-fast-user-switching` | Hide Fast User Switching Option | ui | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowslogon#hidefastuserswitching) |
| `updates.drivers.exclude-from-updates` | Exclude Hardware Drivers from Windows Update | updates | Low | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings#do-not-include-drivers-with-windows-updates) |
| `updates.reboot.disable-auto-reboot-logged-on` | Prevent Forced Auto-Reboot While Logged On | updates | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings#no-auto-reboot-with-logged-on-users-for-scheduled-automatic-updates-installations) |
| `updates.au.notify-download` | Notify Before Downloading Windows Updates | updates | Medium | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings#configure-automatic-updates) |
| `updates.peering.metered-connections` | Block Delivery Optimization on Metered Connections | updates | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization-reference) |
| `updates.activehours.enable-policy` | Enforce Active Hours Policy Against Mid-Day Restarts | updates | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings#turn-off-auto-restart-for-updates-during-active-hours) |
| `updates.preview.disable-optional-content` | Disable Optional Preview Updates & Beta Rollouts | updates | Safe | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/deployment/update/waas-wu-settings#enable-optional-updates) |

---

## 2. Security Category Deep-Dive Review

We audited all tweaks inside `Catalog/Tweaks/security.json`.

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
   - **Function**: Prevents standalone execution of legacy VBScript and JScript (`.vbs`/`.js`) files.
   - **Risk Level**: `Medium`.
   - **Warning Added**: "Can break software installers and administrative scripts relying on VBScript or JScript."
   - **Presets**: Removed from all presets to avoid breaking third-party software setups.
4. **`system.security.enforce-smartscreen`**
   - **Target**: `HKLM\SOFTWARE\Policies\Microsoft\Windows\System\EnableSmartScreen` = `1`
   - **Function**: **Enforces** Windows Defender SmartScreen protection across the operating system via policy. It actively strengthens defenses against untrusted downloads and phishing URLs.
   - **Warning Added**: "This setting is enforced by policy and locks the SmartScreen toggle in Windows Security settings."
5. **`system.security.disable-llmnr`**
   - **Target**: `HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient\EnableMulticast` = `0`
   - **Function**: Blocks Link-Local Multicast Name Resolution broadcast queries on local subnets to protect credentials against Responder/MITM hash-poisoning attacks.
6. **`system.security.disable-rdp-auto-enable`**
   - **Target**: `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\fDenyTSConnections` = `1`
   - **Function**: Ensures inbound Remote Desktop connections on port 3389 remain closed.
   - **Warning Added**: "Remote Desktop to this PC will stop working."
7. **`system.security.disable-lockscreen-camera`**
   - **Target**: `HKLM\SOFTWARE\Policies\Microsoft\Windows\Personalization\NoLockScreenCamera` = `1`
   - **Function**: Disables hardware camera activation while the screen is locked, protecting physical workstation privacy.

*(Note: Fast User Switching was moved to `Catalog/Tweaks/ui.json` under `ui.logon.hide-fast-user-switching`, because fast user switching already requires a password and hiding it is an interface preference, not a security vulnerability fix).*

---

## 3. Distribution Packages and Binary Sizes

We produced both self-contained portable releases (with single-file compression enabled) and lightweight framework-dependent releases for users who already have .NET 10 Desktop Runtime installed.

| Component / Archive | Previous Size | New Compressed Size | Size Reduction | Notes |
|---|---|---|---|---|
| `Tweakora.exe` (Self-Contained Portable) | 148.2 MB | **68.2 MB** | **-54%** | Includes .NET 10 runtime + WPF libraries in one executable |
| `tweakora-cli.exe` (Self-Contained Portable) | 74.5 MB | **38.0 MB** | **-49%** | Includes .NET 10 runtime in one CLI executable |
| `Tweakora.exe` (Small / Framework-Dependent) | N/A | **8.7 MB** | **-94%** | Needs .NET 10 Desktop Runtime installed |
| `tweakora-cli.exe` (Small / Framework-Dependent) | N/A | **1.2 MB** | **-98%** | Needs .NET 10 Runtime installed |
| `Tweakora-v0.1.0-win-x64-portable.zip` | 94.4 MB | **59.6 MB** | **-37%** | Contains portable App + CLI + Catalog + LICENSE + README |
| `Tweakora-v0.1.0-win-x64-small.zip` | N/A | **3.25 MB** | **-96%** | Lightweight package for systems with .NET 10 |

---

## 4. Pending Verification (`docs/NEEDS_VERIFICATION.md`)

In adherence with the Quality Rule (*"It is better to ship 79 verified tweaks than 300 risky ones"*), the following tweaks remain isolated in `docs/NEEDS_VERIFICATION.md` until dedicated VM stress-testing is performed:

1. **`network.tcp.autotuning-experimental`**:
   - *Reason*: TCP window auto-tuning level variations can cause packet drops on specific cellular or satellite NIC adapters.
2. **`performance.system.disable-startup-delay`**:
   - *Reason*: Community tweak (`StartupDelayInMSec` under Explorer\Serialize). Lacks official Microsoft Learn documentation.
3. **`performance.system.clear-pagefile-shutdown`**:
   - *Reason*: High risk of extreme shutdown delays on systems with large memory pools.
4. **`performance.memory.svchost-split-override`**:
   - *Reason*: Grouping svchost processes on low-RAM systems requires testing to verify services do not crash when co-located in a single process.
5. **`system.kernel.mitigations-speculative-execution`**:
   - *Reason*: Disabling Spectre/Meltdown CPU mitigations for performance gains presents serious security risks and is strictly prohibited from the default catalog.
6. **`ui.explorer.compact-view`**:
   - *Reason*: Relies on undocumented File Explorer state flags; lacks official MDM/CSP or MS Learn documentation.
7. **`ui.system.disable-snap-assist`**:
   - *Reason*: Undocumented registry values for Snap Assist layout popups across Windows 11 feature updates.

---

## 5. Test Suite & Quality Metrics

- **Unit Tests**: **35 passed**, 0 failed, 0 skipped (`src/Tweakora.Tests`).
  - Action Handlers: Registry (Apply, Undo, Multiple Baseline Preservation)
  - Platform Safety: All 5 real Windows platform services throw `InvalidOperationException` in test mode
  - Services: Apply, Undo, Baseline preservation
  - Scheduled Tasks: Apply, Undo, Baseline preservation
  - File Cleanup: Dry-run and whitelisted directory execution
  - Completeness: Automated test loading all 79 catalog tweaks verifying `id`, `name`, `description`, `detect`, `apply`, `undo`, and `source`
- **Catalog Schema Validator**: 79/79 tweaks strictly conform to JSON Schema Draft 7 with zero errors (`tools/CatalogValidator`).
- **Catalog Source Checker**: 79/79 documentation URLs verified live and return HTTP 200 OK (`tools/SourceChecker`).
- **FlaUI Automated UI Tests**: Complete end-to-end audit test suite (`src/Tweakora.UiTests`) covering launch, navigation, error verification, search, filtering, staging, dry-run diff dialog, command palette (theme toggle), window resize, and navigation stress/memory verification. All tests passed (2m 21s).
- **Process Health**: Single-file distribution executable (`dist\Tweakora.exe`) runs autonomously for 10 seconds with valid window handle (`MainWindowHandle != 0`).

---

## 6. Questions for Ziad

1. **GitHub Repository**: When you are ready to publish Tweakora to GitHub, do you want us to set up GitHub Actions CI for automated build and release uploading?
2. **Catalog Additions**: Are there specific additional tools or Windows features you would like added in the next release (v0.2.0)?
3. **Inno Setup**: When you install Inno Setup on your workstation, we can automatically compile `installer\tweakora.iss` to produce `Tweakora-Setup-v0.1.0-x64.exe`.

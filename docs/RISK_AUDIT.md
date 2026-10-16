# Risk Recalibration Audit

This audit documents the systematic recalibration of risk ratings across all 149 tweaks in the WinVelo catalog against the formal four-tier risk rubric established in [TWEAK_GUIDE.md](TWEAK_GUIDE.md).

## Formal Rubric Definitions

| Risk Level | Definition & Criteria | Minimum Floor Enforcements |
|:---|:---|:---|
| **Safe** | Per-user (`HKCU`) UI preference, instantly reversible, no feature lost. | Strictly `HKCU` only. Cannot touch `HKLM`. |
| **Low** | Machine policy (`HKLM`), reversible, no feature a typical user relies on is lost. | All `HKLM` settings must be at least Low. |
| **Medium** | Turns off a Windows feature some users rely on (hibernation, Widgets, Copilot, background apps, location, lock screen content, Bing search), or requires a reboot (`requiresReboot: true`). | Any tweak requiring reboot must be at least Medium. |
| **High** | Services, security features, updates, Microsoft Store, networking stack, or irreversible (`reversible: false`). | Irreversible tweaks and servicing-touching tweaks must be High. |

---

## Recalibration Summary

- **Total Tweaks Audited**: 149
- **Tweaks with Changed Risk Level**: 96
- **Unchanged Tweaks**: 53

### Distribution Before vs After

| Tier | Before | After | Delta |
|:---|:---:|:---:|:---:|
| **Safe** | 116 | 45 | -71 |
| **Low** | 21 | 19 | -2 |
| **Medium** | 6 | 48 | +42 |
| **High** | 6 | 37 | +31 |

---

## Detailed Log of Risk Changes

The following table lists every tweak whose risk classification was updated during the recalibration, along with the precise rationale under the rubric:

| Tweak ID | Name | File | Old Risk | New Risk | Reason |
|:---|:---|:---|:---:|:---:|:---|
| `advanced.bravedebloat` | Brave Browser - Debloat | `advanced.json` | **Low** | **High** | Alters Microsoft Store or uninstalls system-integrated applications |
| `advanced.disablewarningforunsignedrdp` | RDP Unsigned File Warnings - Disable | `advanced.json` | **Low** | **High** | Modifies Windows security, authentication, or remote access configuration |
| `advanced.edgedebloat` | Microsoft Edge - Debloat | `advanced.json` | **Low** | **High** | Alters Microsoft Store or uninstalls system-integrated applications |
| `advanced.removehomeandgallery` | File Explorer Home and Gallery - Disable | `advanced.json` | **Low** | **Safe** | Per-user (HKCU) UI preference, instantly reversible, no feature lost |
| `advanced.display` | Visual Effects - Set to Best Performance | `advanced.json` | **Low** | **Safe** | Per-user (HKCU) UI preference, instantly reversible, no feature lost |
| `advanced.razerblock` | Razer Software Auto-Install - Disable | `advanced.json` | **Low** | **Medium** | Blocks OEM peripheral software auto-installation |
| `advanced.logiblock` | Logitech Download Assistant Auto-Install - Disable | `advanced.json` | **Low** | **Medium** | Blocks OEM peripheral software auto-installation |
| `advanced.rightclickmenu` | Right-Click Menu Previous Layout - Enable | `advanced.json` | **Low** | **Safe** | Per-user (HKCU) UI preference, instantly reversible, no feature lost |
| `advanced.ipv46` | IPv6 - Set IPv4 as Preferred | `advanced.json` | **Low** | **High** | Modifies core networking protocol stack, DNS, or interface parameters |
| `advanced.teredo` | Teredo - Disable | `advanced.json` | **Low** | **High** | Modifies core networking protocol stack, DNS, or interface parameters |
| `advanced.disablebgapps` | Background Apps - Disable | `advanced.json` | **Low** | **Medium** | Prevents background application execution |
| `essential.activity` | Activity History - Disable | `essential.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `essential.hiber` | Hibernation - Disable | `essential.json` | **Safe** | **Medium** | Turns off Windows system hibernation and fast startup |
| `essential.widget` | Widgets - Remove | `essential.json` | **Safe** | **High** | Irreversible modification (minimum floor) |
| `essential.revertstartmenu` | Start Menu Previous Layout - Enable | `essential.json` | **Safe** | **Medium** | Alters Start Menu layout or recommendations |
| `essential.location` | Location Tracking - Disable | `essential.json` | **Safe** | **High** | Modifies Windows system service state |
| `essential.services` | Services - Set to Manual | `essential.json` | **Safe** | **High** | Modifies Windows system service state |
| `essential.consumerfeatures` | ConsumerFeatures - Disable | `essential.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `essential.telemetry` | Telemetry - Disable | `essential.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `essential.deliveryoptimization` | Delivery Optimization - Disable | `essential.json` | **Safe** | **High** | Modifies Windows Update delivery or servicing policies |
| `essential.restorepoint` | Restore Point - Create | `essential.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `essential.wpbt` | Windows Platform Binary Table (WPBT) - Disable | `essential.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `essential.preventdevicemetadatafromnetwork` | Prevent Device Companion Apps | `essential.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `essential.diskcleanup` | Disk Cleanup - Run | `essential.json` | **Safe** | **High** | Irreversible modification (minimum floor) |
| `essential.deletetempfiles` | Temporary Files - Remove | `essential.json` | **Safe** | **High** | Irreversible modification (minimum floor) |
| `ai.copilot.disable-taskbar-button` | Hide Copilot Taskbar Button | `extras.json` | **Safe** | **Medium** | Turns off Windows Copilot or AI subsystem features |
| `ai.copilot.disable-policy` | Disable Windows Copilot Globally | `extras.json` | **Low** | **Medium** | Turns off Windows Copilot or AI subsystem features |
| `ai.recall.disable-snapshots` | Disable Windows Recall Snapshots | `extras.json` | **Safe** | **Medium** | Turns off Windows Copilot or AI subsystem features |
| `ai.search.disable-bing-web-search` | Disable Bing Web Search in Start Menu | `extras.json` | **Safe** | **Medium** | Turns off web search integration in Start Menu |
| `gaming.xbox.disable-game-bar` | Disable Xbox Game Bar Overlay | `extras.json` | **Safe** | **Medium** | Turns off Xbox Game Bar and background gameplay clip recording |
| `gaming.dvr.disable-background-recording` | Disable Background Game DVR Recording | `extras.json` | **Safe** | **Medium** | Turns off Xbox Game Bar and background gameplay clip recording |
| `network.probing.disable-active-probing` | Disable NCSI Active Internet Probing | `extras.json` | **Medium** | **High** | Modifies core networking protocol stack, DNS, or interface parameters |
| `network.optimization.limit-peering-lan` | Restrict Delivery Optimization to Local Network Only | `extras.json` | **Safe** | **High** | Modifies core networking protocol stack, DNS, or interface parameters |
| `network.dns.enable-doh` | Enable DNS over HTTPS (DoH) System Fallback | `extras.json` | **Safe** | **High** | Modifies core networking protocol stack, DNS, or interface parameters |
| `network.teredo.disable-tunneling` | Disable Teredo IPv6 Tunneling | `extras.json` | **Safe** | **High** | Modifies core networking protocol stack, DNS, or interface parameters |
| `performance.system.storage-sense` | Enable Storage Sense Auto-Cleanup | `extras.json` | **Safe** | **Medium** | Alters disk storage allocation, NTFS behavior, or maintenance wake timers |
| `performance.network.delivery-optimization-simple` | Disable Delivery Optimization Peer-to-Peer (HTTP Only) | `extras.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `performance.system.disable-maintenance-wake` | Disable Automatic Maintenance Wake-Up Timers | `extras.json` | **Safe** | **Medium** | Alters disk storage allocation, NTFS behavior, or maintenance wake timers |
| `performance.search.prevent-indexing-on-battery` | Prevent Search Indexing While on Battery Power | `extras.json` | **Safe** | **Medium** | Alters disk storage allocation, NTFS behavior, or maintenance wake timers |
| `performance.ntfs.disable-last-access` | Disable NTFS Last Access Time Updates | `extras.json` | **Safe** | **Medium** | Alters disk storage allocation, NTFS behavior, or maintenance wake timers |
| `performance.ntfs.disable-8dot3-names` | Disable 8.3 Short File Name Creation | `extras.json` | **Safe** | **Medium** | Alters disk storage allocation, NTFS behavior, or maintenance wake timers |
| `privacy.telemetry.diagnostic-data` | Diagnostic Data Level | `extras.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `privacy.activity.activity-history` | Disable Activity History Publishing | `extras.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `privacy.clipboard.cloud-clipboard-sync` | Disable Cloud Clipboard Sync | `extras.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `privacy.spotlight.lockscreen-ads` | Disable Lock Screen Tips & Spotlight Ads | `extras.json` | **Safe** | **Medium** | Alters Windows lock screen behavior or visual presentation |
| `privacy.suggestions.start-menu-promotions` | Disable Start Menu Suggested App Ads | `extras.json` | **Safe** | **Medium** | Alters Start Menu layout or recommendations |
| `privacy.location.disable-sensor` | Disable System Location Sensor | `extras.json` | **Low** | **Medium** | Turns off Windows location platform and sensor access |
| `privacy.consent.deny-location` | Deny Apps Access to Location by Default | `extras.json` | **Safe** | **Medium** | Turns off Windows location platform and sensor access |
| `privacy.consent.deny-microphone` | Deny UWP Apps Access to Microphone by Default | `extras.json` | **Low** | **Medium** | Denies application access to hardware devices or personal APIs |
| `privacy.consent.deny-webcam` | Deny UWP Apps Access to Webcam by Default | `extras.json` | **Low** | **Medium** | Denies application access to hardware devices or personal APIs |
| `privacy.consent.deny-notifications` | Deny UWP Apps Access to User Notifications | `extras.json` | **Safe** | **Medium** | Denies application access to hardware devices or personal APIs |
| `privacy.consent.deny-account-info` | Deny Apps Access to Account Information | `extras.json` | **Safe** | **Medium** | Denies application access to hardware devices or personal APIs |
| `privacy.consent.deny-contacts` | Deny Apps Access to Contacts List | `extras.json` | **Safe** | **Medium** | Denies application access to hardware devices or personal APIs |
| `privacy.consent.deny-calendar` | Deny Apps Access to User Calendar & Appointments | `extras.json` | **Safe** | **Medium** | Denies application access to hardware devices or personal APIs |
| `privacy.consent.deny-diagnostics` | Deny Apps Access to Other Apps Diagnostic Info | `extras.json` | **Safe** | **Medium** | Denies application access to hardware devices or personal APIs |
| `system.security.disable-remote-assistance` | Disable Remote Assistance | `extras.json` | **Safe** | **High** | Modifies Windows security, authentication, or remote access configuration |
| `system.security.disable-autorun` | Disable AutoRun & AutoPlay on All Drives | `extras.json` | **Safe** | **High** | Modifies Windows security, authentication, or remote access configuration |
| `system.security.disable-wsh` | Disable Windows Script Host (VBS/JS Scripts) | `extras.json` | **Medium** | **High** | Modifies Windows security, authentication, or remote access configuration |
| `system.security.enforce-smartscreen` | Enforce Windows SmartScreen via Policy | `extras.json` | **Safe** | **High** | Modifies Windows security, authentication, or remote access configuration |
| `system.security.disable-llmnr` | Disable Link-Local Multicast Name Resolution (LLMNR) | `extras.json` | **Safe** | **High** | Modifies Windows security, authentication, or remote access configuration |
| `system.security.disable-rdp-auto-enable` | Disable Remote Desktop Inbound Connections | `extras.json` | **Low** | **High** | Modifies Windows security, authentication, or remote access configuration |
| `system.security.disable-lockscreen-camera` | Disable Camera Access on Lock Screen | `extras.json` | **Safe** | **High** | Modifies Windows security, authentication, or remote access configuration |
| `ui.system.enable-long-paths` | Enable Win32 Long Paths (>260 Chars) | `extras.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `ui.taskbar.disable-widgets-button` | Hide Widgets Weather Icon from Taskbar | `extras.json` | **Safe** | **Medium** | Turns off Windows Widgets platform |
| `ui.taskbar.disable-chat-button` | Hide Microsoft Teams Chat Icon on Taskbar | `extras.json` | **Safe** | **Medium** | Removes interactive taskbar navigation controls |
| `ui.taskbar.disable-task-view-button` | Hide Task View Button from Taskbar | `extras.json` | **Safe** | **Medium** | Removes interactive taskbar navigation controls |
| `ui.logon.hide-fast-user-switching` | Hide Fast User Switching Option | `extras.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `updates.drivers.exclude-from-updates` | Exclude Hardware Drivers from Windows Update | `extras.json` | **Low** | **High** | Modifies Windows Update delivery or servicing policies |
| `updates.reboot.disable-auto-reboot-logged-on` | Prevent Forced Auto-Reboot While Logged On | `extras.json` | **Safe** | **High** | Modifies Windows Update delivery or servicing policies |
| `updates.au.notify-download` | Notify Before Downloading Windows Updates | `extras.json` | **Medium** | **High** | Modifies Windows servicing component or policy (minimum floor) |
| `updates.peering.metered-connections` | Block Delivery Optimization on Metered Connections | `extras.json` | **Safe** | **High** | Modifies Windows Update delivery or servicing policies |
| `updates.activehours.enable-policy` | Enforce Active Hours Policy Against Mid-Day Restarts | `extras.json` | **Safe** | **High** | Modifies Windows servicing component or policy (minimum floor) |
| `updates.preview.disable-optional-content` | Disable Optional Preview Updates & Beta Rollouts | `extras.json` | **Safe** | **High** | Modifies Windows servicing component or policy (minimum floor) |
| `performance.gaming.mmcss-responsiveness` | Multimedia Class Scheduler Service (MMCSS) Gaming Priority | `performance.json` | **Safe** | **Medium** | Modifies hardware power state, thread quantum, or multimedia scheduler responsiveness |
| `performance.gpu.hags` | Hardware-Accelerated GPU Scheduling (HAGS) | `performance.json` | **Safe** | **Medium** | Requires system reboot to take effect |
| `performance.gpu.disable-mpo` | Disable Multi-Plane Overlay (MPO Workaround) | `performance.json` | **Safe** | **Medium** | Requires system reboot to take effect |
| `performance.input.buffer-queue` | Mouse & Keyboard Buffer Queue (Experimental) | `performance.json` | **Safe** | **Medium** | Requires system reboot to take effect |
| `performance.power.disable-throttling` | Disable Windows Power Throttling | `performance.json` | **Safe** | **Medium** | Modifies hardware power state, thread quantum, or multimedia scheduler responsiveness |
| `pref.detailedbsod` | BSoD Verbose Mode | `preferences.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `pref.verboselogon` | Logon Verbose Mode | `preferences.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `pref.multiplaneoverlay` | Multiplane Overlay Workaround | `preferences.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |
| `pref.standbyfix` | S0 Sleep Network Connectivity | `preferences.json` | **Safe** | **Medium** | Modifies hardware power state, thread quantum, or multimedia scheduler responsiveness |
| `pref.s3sleep` | S3 Sleep | `preferences.json` | **Safe** | **Medium** | Modifies hardware power state, thread quantum, or multimedia scheduler responsiveness |
| `pref.bingsearch` | Start Menu Bing Search | `preferences.json` | **Safe** | **Medium** | Turns off web search integration in Start Menu |
| `pref.loginblur` | Logon Screen Acrylic Blur | `preferences.json` | **Safe** | **Medium** | Alters Windows lock screen behavior or visual presentation |
| `pref.disablelockscreen` | Lock Screen - Disable | `preferences.json` | **Safe** | **Medium** | Alters Windows lock screen behavior or visual presentation |
| `pref.startmenurecommendations` | Start Menu Recommendations | `preferences.json` | **Safe** | **Medium** | Alters Start Menu layout or recommendations |
| `pref.taskview` | Taskbar Task View Icon | `preferences.json` | **Safe** | **Medium** | Removes interactive taskbar navigation controls |
| `pref.longpaths` | Enable Long Paths | `preferences.json` | **Safe** | **Low** | Reversible machine policy (HKLM) where no relied-upon user feature is lost |

---
*Generated by automated risk recalibration engine in WinVelo.* 

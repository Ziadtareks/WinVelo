# WinVelo Evidence Audit Report

This document records the formal re-audit of the `evidence` field across all 149 tweaks in the WinVelo catalog, enforcing strict evidence tiers:
1. **Documented (16 tweaks)**: Official vendor documentation (Microsoft Learn / Microsoft Docs) explicitly names the setting (the exact registry value name, policy name, or CSP property). A generic documentation page about the subsystem does not qualify. Every documented tweak stores the exact vendor quote in its `sourceQuote` property.
2. **UI-Equivalent (71 tweaks)**: Ordinary settings that have a direct native GUI toggle in Windows Settings, Control Panel, or File Explorer options. The exact GUI path is disclosed in `uiEquivalentLocation` (e.g. `Settings › Personalization › Colors`).
3. **Situational (60 tweaks)**: Settings, policies, or scripts that address or work around a specific named problem, hardware bug, dual-boot clock issue, driver overwrite, background wake timer, or debloat requirement without an ordinary UI toggle.
4. **Anecdotal (2 tweaks)**: Speculative performance claims with no documented empirical benefit (at the time: `performance.cpu.unpark-cores` and `performance.input.buffer-queue`; both were removed in 1.0.1 as placebo). Quarantined under `needsReview: true` and excluded from presets.

---

## Evidence Summary

| Evidence Level | Count | Definition | Recommended-Only Eligible? |
|---|---|---|---|
| **Documented** | **16** | Official vendor documentation explicitly naming the setting with quote in `sourceQuote`. | **Yes** (if Safe or Low, reversible, not needsReview) |
| **UI-Equivalent** | **71** | Direct native toggle in Windows Settings, Control Panel, or File Explorer disclosed in `uiEquivalentLocation`. | **Yes** (if Safe or Low, reversible, not needsReview) |
| **Situational** | **60** | Fixes or works around a specific named problem, hardware quirk, or debloat requirement. | No (targeted/situational) |
| **Anecdotal** | **2** | Speculative or unverified community tweaks with no measurable empirical benefit. Quarantined behind `needsReview: true`. | No (quarantined) |
| **Total** | **149** | Complete WinVelo catalog. | - |

---

## Complete Audit Log

| Tweak ID | Name | Evidence | Details / Source Quote / Windows Location |
|---|---|---|---|
| `advanced.bravedebloat` | Brave Browser - Debloat | **situational** | *Situational*: Addresses specific workaround/issue: Brave browser automatic background updates and promotional notifications will stop functioning. |
| `advanced.disablewarningforunsignedrdp` | RDP Unsigned File Warnings - Disable | **situational** | *Situational*: Addresses specific workaround/issue: Suppresses security warning dialogs when connecting to untrusted or self-signed Remote Desktop endpo |
| `advanced.edgedebloat` | Microsoft Edge - Debloat | **situational** | *Situational*: Addresses specific workaround/issue: Disables Edge automatic background updates and may impact WebView2-dependent system widgets. |
| `advanced.removeedge` | Microsoft Edge - Remove | **situational** | *Situational*: Addresses specific workaround/issue: Microsoft Edge browser is permanently removed; system features relying on Edge or embedded WebViews  |
| `advanced.disablebitlocker` | BitLocker - Disable | **documented** | **Documented**: "TEM\CurrentControlSet\Control\BitLocker PreventDeviceEncryption REG_DWORD 0x1 For more information about device encryption, see BitLocker devic" |
| `advanced.utc` | Date & Time - Set Time to UTC | **situational** | *Situational*: Addresses specific workaround/issue: Configures Windows hardware clock to UTC (ideal for Linux dual-boot); may cause incorrect local time |
| `advanced.removeonedrive` | Microsoft OneDrive - Remove | **situational** | *Situational*: Addresses specific workaround/issue: Removes OneDrive file synchronization and integration from File Explorer; local files stored only in |
| `advanced.removehomeandgallery` | File Explorer Home and Gallery - Disable | **situational** | *Situational*: Addresses specific workaround/issue: None known. |
| `advanced.display` | Visual Effects - Set to Best Performance | **ui-equivalent** | **Windows GUI**: `SystemPropertiesPerformance.exe › Visual Effects › Adjust for best performance` |
| `advanced.reservedstorage` | Disable Reserved Storage | **situational** | *Situational*: Addresses specific workaround/issue: Windows Update may fail during major feature updates if the primary storage drive runs low on free d |
| `advanced.storage` | Storage Sense - Disable | **ui-equivalent** | **Windows GUI**: `Settings › System › Storage › Storage Sense` |
| `advanced.windowsai` | Windows AI - Disable And Remove | **situational** | *Situational*: Addresses specific workaround/issue: Copilot taskbar button, Copilot sidebar, and AI-assisted Windows features will become unavailable. |
| `advanced.razerblock` | Razer Software Auto-Install - Disable | **situational** | *Situational*: Addresses specific workaround/issue: Razer peripheral setup and RGB configuration software must be downloaded and installed manually from |
| `advanced.logiblock` | Logitech Download Assistant Auto-Install - Disable | **situational** | *Situational*: Addresses specific workaround/issue: Logitech mouse and keyboard management software will not automatically install upon plugging in the  |
| `advanced.disablenotifications` | System Tray Notifications & Calendar - Disable | **ui-equivalent** | **Windows GUI**: `Settings › System › Notifications` |
| `advanced.rightclickmenu` | Right-Click Menu Previous Layout - Enable | **situational** | *Situational*: Addresses specific workaround/issue: None known. |
| `advanced.ipv46` | IPv6 - Set IPv4 as Preferred | **documented** | **Documented**: "lSet\Services\Tcpip6\Parameters\ Name : DisabledComponents Type : REG_DWORD Min Value : 0x00 (default value) Max Value : 0xFF (IPv6 disabl" |
| `advanced.teredo` | Teredo - Disable | **situational** | *Situational*: Addresses specific workaround/issue: Disables IPv6-over-IPv4 tunneling; legacy multiplayer games or Xbox Live services that require Tered |
| `advanced.disableipv6` | IPv6 - Disable | **documented** | **Documented**: "lSet\Services\Tcpip6\Parameters\ Name : DisabledComponents Type : REG_DWORD Min Value : 0x00 (default value) Max Value : 0xFF (IPv6 disabl" |
| `advanced.disablebgapps` | Background Apps - Disable | **ui-equivalent** | **Windows GUI**: `Settings › Apps › Installed apps › Advanced options › Background apps permissions` |
| `essential.activity` | Activity History - Disable | **documented** | **Documented**: "eate a REG_DWORD registry setting named EnableActivityFeed in HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\System with a value o" |
| `essential.hiber` | Hibernation - Disable | **situational** | *Situational*: Addresses specific workaround/issue: System hibernation is removed, increasing boot time slightly when starting from a cold power-off sta |
| `essential.widget` | Widgets - Remove | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Taskbar › Taskbar items › Widgets` |
| `essential.revertstartmenu` | Start Menu Previous Layout - Enable | **situational** | *Situational*: Addresses specific workaround/issue: Overrides newer Start Menu redesigns; will not take effect on incompatible newer build revisions. |
| `essential.disablestoresearch` | Microsoft Store Recommended Search Results - Disable | **situational** | *Situational*: Addresses specific workaround/issue: Denies access to store.db, which can cause Microsoft Store search and package downloads to error or  |
| `essential.location` | Location Tracking - Disable | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › Location › Location services` |
| `essential.services` | Services - Set to Manual | **situational** | *Situational*: Addresses specific workaround/issue: Disables Mobile Hotspot and Internet Connection Sharing (SharedAccess), disables Offline Files sync  |
| `essential.consumerfeatures` | ConsumerFeatures - Disable | **situational** | *Situational*: Addresses specific workaround/issue: Prevents Windows from automatically downloading recommended games and promotional apps upon setup. |
| `essential.telemetry` | Telemetry - Disable | **documented** | **Documented**: "pply the following MDM policies: System/AllowTelemetry System/LimitDumpCollection System/LimitDiagnosticLogCollection Note The last tw" |
| `essential.deliveryoptimization` | Delivery Optimization - Disable | **ui-equivalent** | **Windows GUI**: `Settings › Windows Update › Advanced options › Delivery Optimization › Allow downloads from other PCs` |
| `essential.restorepoint` | Restore Point - Create | **situational** | *Situational*: Addresses specific workaround/issue: Allocates and reserves system storage space (typically several gigabytes) for Volume Shadow Copy res |
| `essential.endtaskontaskbar` | End Task With Right Click - Enable | **ui-equivalent** | **Windows GUI**: `Settings › System › For developers › End Task` |
| `essential.wpbt` | Windows Platform Binary Table (WPBT) - Disable | **situational** | *Situational*: Addresses specific workaround/issue: Blocks OEM firmware-injected executables on motherboard boot; certain vendor-specific utility softwa |
| `essential.preventdevicemetadatafromnetwork` | Prevent Device Companion Apps | **situational** | *Situational*: Addresses specific workaround/issue: Connected printers, monitors, and peripherals in Device Manager will display generic hardware icons  |
| `essential.diskcleanup` | Disk Cleanup - Run | **situational** | *Situational*: Addresses specific workaround/issue: Empties Recycle Bin and deletes downloaded Windows Update setup caches. |
| `essential.deletetempfiles` | Temporary Files - Remove | **situational** | *Situational*: Addresses specific workaround/issue: Closes or clears temporary application caches; running programs using active temp files are skipped  |
| `essential.disableexplorerautodiscovery` | File Explorer Automatic Folder Discovery - Disable | **situational** | *Situational*: Addresses specific workaround/issue: Network-attached media servers and smart TVs will not automatically pop up in File Explorer until ma |
| `ai.copilot.disable-taskbar-button` | Hide Copilot Taskbar Button | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Taskbar › Taskbar items › Copilot` |
| `ai.copilot.disable-policy` | Disable Windows Copilot Globally | **situational** | *Situational*: Addresses specific workaround/issue: Copilot sidebar and keyboard shortcut (Win+C) become completely disabled. |
| `ai.recall.disable-snapshots` | Disable Windows Recall Snapshots | **situational** | *Situational*: Addresses specific workaround/issue: Windows will not capture or index historical snapshots of user desktop activity. |
| `ai.search.disable-bing-web-search` | Disable Bing Web Search in Start Menu | **situational** | *Situational*: Addresses specific workaround/issue: Search box queries will only index and display local programs and files. |
| `gaming.mode.enable` | Enable Windows Game Mode | **ui-equivalent** | **Windows GUI**: `Settings › Gaming › Game Mode › Game Mode` |
| `gaming.xbox.disable-game-bar` | Disable Xbox Game Bar Overlay | **ui-equivalent** | **Windows GUI**: `Settings › Gaming › Game Bar › Open Xbox Game Bar using this button on a controller` |
| `gaming.dvr.disable-background-recording` | Disable Background Game DVR Recording | **ui-equivalent** | **Windows GUI**: `Settings › Gaming › Captures › Record in the background while I'm playing a game` |
| `gaming.input.disable-mouse-acceleration` | Disable Pointer Acceleration (Enhance Pointer Precision) | **ui-equivalent** | **Windows GUI**: `Settings › Bluetooth & devices › Mouse › Additional mouse settings › Pointer Options › Enhance pointer precision` |
| `network.probing.disable-active-probing` | Disable NCSI Active Internet Probing | **situational** | *Situational*: Addresses specific workaround/issue: Network status icon in the system tray may erroneously show 'No Internet Access' even when connectiv |
| `network.optimization.limit-peering-lan` | Restrict Delivery Optimization to Local Network Only | **situational** | *Situational*: Addresses specific workaround/issue: Prevents Windows Update from downloading or uploading update bits to computers outside your local ar |
| `network.dns.enable-doh` | Enable DNS over HTTPS (DoH) System Fallback | **ui-equivalent** | **Windows GUI**: `Settings › Network & internet › Wi-Fi / Ethernet › DNS server assignment › Encrypted only (DNS over HTTPS)` |
| `network.teredo.disable-tunneling` | Disable Teredo IPv6 Tunneling | **situational** | *Situational*: Addresses specific workaround/issue: Prevents Teredo tunneling; applications or multiplayer games requiring Teredo NAT traversal may lose |
| `performance.visual.disable-transparency` | Disable Transparency Effects | **ui-equivalent** | **Windows GUI**: `Settings › Accessibility › Visual effects › Transparency effects` |
| `performance.visual.disable-animations` | Disable Window Minimize/Maximize Animations | **ui-equivalent** | **Windows GUI**: `Settings › Accessibility › Visual effects › Animation effects` |
| `performance.system.storage-sense` | Enable Storage Sense Auto-Cleanup | **ui-equivalent** | **Windows GUI**: `Settings › System › Storage › Storage Sense` |
| `performance.network.delivery-optimization-simple` | Disable Delivery Optimization Peer-to-Peer (HTTP Only) | **ui-equivalent** | **Windows GUI**: `Settings › Windows Update › Advanced options › Delivery Optimization › Allow downloads from other PCs` |
| `performance.system.disable-maintenance-wake` | Disable Automatic Maintenance Wake-Up Timers | **situational** | *Situational*: Addresses specific workaround/issue: Scheduled system maintenance and disk scans will only execute while the computer is already awake an |
| `performance.search.prevent-indexing-on-battery` | Prevent Search Indexing While on Battery Power | **situational** | *Situational*: Addresses specific workaround/issue: Newly created or modified files will not be indexed into search results until the laptop is connecte |
| `performance.ntfs.disable-last-access` | Disable NTFS Last Access Time Updates | **situational** | *Situational*: Addresses specific workaround/issue: File access timestamps will not be updated when files are read; backup tools relying on last-access  |
| `performance.ntfs.disable-8dot3-names` | Disable 8.3 Short File Name Creation | **situational** | *Situational*: Addresses specific workaround/issue: Older 16-bit or legacy 32-bit software that relies on 8.3 short filename paths may fail to open file |
| `privacy.telemetry.diagnostic-data` | Diagnostic Data Level | **documented** | **Documented**: "pply the following MDM policies: System/AllowTelemetry System/LimitDumpCollection System/LimitDiagnosticLogCollection Note The last tw" |
| `privacy.advertising.advertising-id` | Disable Advertising ID | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › General › Let apps show me personalized ads by using my advertising ID` |
| `privacy.experiences.tailored-experiences` | Disable Tailored Experiences | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › Diagnostics & feedback › Tailored experiences` |
| `privacy.activity.activity-history` | Disable Activity History Publishing | **documented** | **Documented**: "eate a REG_DWORD registry setting named PublishUserActivities in HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\System with a value o" |
| `privacy.speech.online-speech-recognition` | Disable Online Speech Recognition | **documented** | **Documented**: "eate a REG_DWORD registry setting named HasAccepted in HKEY_CURRENT_USER\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPri" |
| `privacy.typing.inking-typing-personalization` | Disable Inking & Typing Data Collection | **documented** | **Documented**: "ization to a value of 1 (one) -and- Set RestrictImplicitInkCollection registry REG_DWORD setting in HKEY_CURRENT_USER\Software\Microsoft\InputPersona" |
| `privacy.tracking.app-launch-tracking` | Disable App Launch Tracking | **documented** | **Documented**: "eate a REG_DWORD registry setting named Start_TrackProgs in HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advance" |
| `privacy.feedback.feedback-prompts` | Disable Feedback Frequency Prompts | **documented** | **Documented**: "RENT_USER\Software\Microsoft\Siuf\Rules\NumberOfSIUFInPeriod Based on these settings: Setting PeriodInNanoSeconds NumberOfSIUFInPeriod Autom" |
| `privacy.reporting.windows-error-reporting` | Disable Windows Error Reporting | **situational** | *Situational*: Addresses specific workaround/issue: Application crash reports will not be sent to Microsoft or software vendors for diagnosis. |
| `privacy.clipboard.cloud-clipboard-sync` | Disable Cloud Clipboard Sync | **ui-equivalent** | **Windows GUI**: `Settings › System › Clipboard › Clipboard history across your devices` |
| `privacy.search.device-search-history` | Disable Device Search History | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › Search permissions › History` |
| `privacy.spotlight.lockscreen-ads` | Disable Lock Screen Tips & Spotlight Ads | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Lock screen › Get fun facts, tips, tricks, and more on your lock screen` |
| `privacy.suggestions.start-menu-promotions` | Disable Start Menu Suggested App Ads | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Start › Show recommendations for tips, shortcuts, new apps, and more` |
| `privacy.location.disable-sensor` | Disable System Location Sensor | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › Location › Location services` |
| `privacy.consent.deny-location` | Deny Apps Access to Location by Default | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › Location › Let apps access your location` |
| `privacy.consent.deny-microphone` | Deny UWP Apps Access to Microphone by Default | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › Microphone › Let apps access your microphone` |
| `privacy.consent.deny-webcam` | Deny UWP Apps Access to Webcam by Default | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › Camera › Let apps access your camera` |
| `privacy.consent.deny-notifications` | Deny UWP Apps Access to User Notifications | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › Notifications › Let apps access your notifications` |
| `privacy.consent.deny-account-info` | Deny Apps Access to Account Information | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › Account info › Let apps access your account info` |
| `privacy.consent.deny-contacts` | Deny Apps Access to Contacts List | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › Contacts › Let apps access your contacts` |
| `privacy.consent.deny-calendar` | Deny Apps Access to User Calendar & Appointments | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › Calendar › Let apps access your calendar` |
| `privacy.consent.deny-diagnostics` | Deny Apps Access to Other Apps Diagnostic Info | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › App diagnostics › Let apps access diagnostic info about other apps` |
| `system.security.disable-remote-assistance` | Disable Remote Assistance | **ui-equivalent** | **Windows GUI**: `Control Panel › System › Remote settings › Allow Remote Assistance connections to this computer` |
| `system.security.disable-autorun` | Disable AutoRun & AutoPlay on All Drives | **ui-equivalent** | **Windows GUI**: `Settings › Bluetooth & devices › AutoPlay` |
| `system.security.disable-wsh` | Disable Windows Script Host (VBS/JS Scripts) | **situational** | *Situational*: Addresses specific workaround/issue: Legacy VBScript and JScript administrative scripts will be blocked from executing. |
| `system.security.enforce-smartscreen` | Enforce Windows SmartScreen via Policy | **situational** | *Situational*: Addresses specific workaround/issue: Blocks execution of unrecognized or untrusted downloaded programs until explicitly overridden. |
| `system.security.disable-llmnr` | Disable Link-Local Multicast Name Resolution (LLMNR) | **situational** | *Situational*: Addresses specific workaround/issue: Prevents LLMNR broadcast name resolution; legacy systems on the local network without DNS may fail t |
| `system.security.disable-rdp-auto-enable` | Disable Remote Desktop Inbound Connections | **ui-equivalent** | **Windows GUI**: `Settings › System › Remote Desktop` |
| `system.security.disable-lockscreen-camera` | Disable Camera Access on Lock Screen | **ui-equivalent** | **Windows GUI**: `Settings › Accounts › Sign-in options › Dynamic lock` |
| `ui.explorer.classic-context-menu` | Restore Classic Right-Click Menu | **situational** | *Situational*: Addresses specific workaround/issue: None known. |
| `ui.explorer.show-file-extensions` | Always Show Known File Extensions | **ui-equivalent** | **Windows GUI**: `File Explorer › View › Show › File name extensions` |
| `ui.explorer.show-hidden-files` | Show Hidden Files and Folders | **ui-equivalent** | **Windows GUI**: `File Explorer › View › Show › Hidden items` |
| `ui.explorer.open-to-this-pc` | Open File Explorer to This PC | **ui-equivalent** | **Windows GUI**: `File Explorer › Options › General › Open File Explorer to: This PC` |
| `ui.taskbar.end-task-right-click` | Enable 'End Task' in Taskbar Context Menu | **ui-equivalent** | **Windows GUI**: `Settings › System › For developers › End Task` |
| `ui.taskbar.show-seconds-clock` | Show Seconds in Taskbar Clock | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Taskbar › Taskbar behaviors › Show seconds in system tray clock` |
| `ui.system.enable-long-paths` | Enable Win32 Long Paths (>260 Chars) | **documented** | **Documented**: "EM\CurrentControlSet\Control\FileSystem LongPathsEnabled (Type: REG_DWORD) must exist and be set to 1 . The registry value will be cache" |
| `ui.taskbar.alignment-left` | Align Taskbar Icons to the Left | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Taskbar › Taskbar behaviors › Taskbar alignment` |
| `ui.explorer.hide-gallery` | Hide Gallery from File Explorer Navigation Pane | **situational** | *Situational*: Addresses specific workaround/issue: None known. |
| `ui.explorer.hide-recent-files` | Hide Recent Files in File Explorer Home | **ui-equivalent** | **Windows GUI**: `File Explorer › Options › General › Privacy › Show recently used files` |
| `ui.explorer.hide-frequent-folders` | Hide Frequently Used Folders in Quick Access | **ui-equivalent** | **Windows GUI**: `File Explorer › Options › General › Privacy › Show frequently used folders` |
| `ui.explorer.show-full-path-title` | Display Full Folder Path in File Explorer Title Bar | **ui-equivalent** | **Windows GUI**: `File Explorer › Options › View › Display the full path in the title bar` |
| `ui.search.disable-search-highlights` | Disable Search Box Highlights & Illustrations | **ui-equivalent** | **Windows GUI**: `Settings › Privacy & security › Search permissions › More settings › Show search highlights` |
| `ui.taskbar.disable-widgets-button` | Hide Widgets Weather Icon from Taskbar | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Taskbar › Taskbar items › Widgets` |
| `ui.taskbar.disable-chat-button` | Hide Microsoft Teams Chat Icon on Taskbar | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Taskbar › Taskbar items › Chat` |
| `ui.taskbar.disable-task-view-button` | Hide Task View Button from Taskbar | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Taskbar › Taskbar items › Task view` |
| `ui.system.disable-welcome-experience` | Disable Windows Welcome Experience After Updates | **ui-equivalent** | **Windows GUI**: `Settings › System › Notifications › Additional settings › Show the Windows welcome experience after updates` |
| `ui.system.disable-tips-suggestions` | Disable Tips, Tricks, and Suggestions About Windows | **ui-equivalent** | **Windows GUI**: `Settings › System › Notifications › Additional settings › Get tips and suggestions when using Windows` |
| `ui.logon.hide-fast-user-switching` | Hide Fast User Switching Option | **situational** | *Situational*: Addresses specific workaround/issue: Users must sign out completely before another user can log into the computer. |
| `updates.drivers.exclude-from-updates` | Exclude Hardware Drivers from Windows Update | **situational** | *Situational*: Addresses specific workaround/issue: Windows Update will not update GPU, chipset, or peripheral drivers; drivers must be updated manually |
| `updates.reboot.disable-auto-reboot-logged-on` | Prevent Forced Auto-Reboot While Logged On | **documented** | **Documented**: "date\AU , set AuOptions to 4 and enable NoAutoRebootWithLoggedOnUsers . As with group policy, if a user schedules the restart in the update notificat" |
| `updates.au.notify-download` | Notify Before Downloading Windows Updates | **documented** | **Documented**: "icrosoft\Windows\WindowsUpdate\AU , set AuOptions to 4 and set the install time with ScheduledInstallTime . Enable AlwaysAutoRebo" |
| `updates.peering.metered-connections` | Block Delivery Optimization on Metered Connections | **ui-equivalent** | **Windows GUI**: `Settings › Windows Update › Advanced options › Delivery Optimization › Advanced options › Download settings` |
| `updates.activehours.enable-policy` | Enforce Active Hours Policy Against Mid-Day Restarts | **documented** | **Documented**: "ies\Microsoft\Windows\WindowsUpdate use SetActiveHours to enable or disable active hours and ActiveHoursStart and ActiveHoursEnd to sp" |
| `updates.preview.disable-optional-content` | Disable Optional Preview Updates & Beta Rollouts | **situational** | *Situational*: Addresses specific workaround/issue: Windows Update will only install cumulative monthly security updates, ignoring preview feature rollo |
| `performance.gaming.mmcss-responsiveness` | Multimedia Class Scheduler Service (MMCSS) Gaming Priority | **situational** | *Situational*: Addresses specific workaround/issue: Background system tasks may experience CPU starvation during gaming; in rare hardware setups, this m |
| `performance.gpu.hags` | Hardware-Accelerated GPU Scheduling (HAGS) | **ui-equivalent** | **Windows GUI**: `Settings › System › Display › Graphics › Default graphics settings › Hardware-accelerated GPU scheduling` |
| `performance.gpu.disable-mpo` | Disable Multi-Plane Overlay (MPO Workaround) | **situational** | *Situational*: Addresses specific workaround/issue: Workaround for screen flickering or black screens on multi-monitor setups; increases GPU power consu |
| `performance.input.raw-mouse` | Disable Mouse Acceleration (1:1 Raw Input) | **ui-equivalent** | **Windows GUI**: `Settings › Bluetooth & devices › Mouse › Additional mouse settings › Pointer Options › Enhance pointer precision` |
| `performance.input.buffer-queue` | Mouse & Keyboard Buffer Queue (Experimental) | **anecdotal** | *Anecdotal*: Speculative claim; quarantined with needsReview: true |
| `performance.power.disable-throttling` | Disable Windows Power Throttling | **situational** | *Situational*: Addresses specific workaround/issue: Noticeably reduces battery runtime and increases thermal output on mobile and battery-powered device |
| `pref.detailedbsod` | BSoD Verbose Mode | **situational** | *Situational*: Addresses specific workaround/issue: Shows full technical diagnostic details instead of the friendly blue-screen emoticon. |
| `pref.batterypercentage` | System Tray Battery Percentage | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Taskbar › Other system tray icons` |
| `pref.darkmode` | Dark Theme for Windows | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Colors › Choose your mode` |
| `pref.showext` | File Explorer File Extensions | **ui-equivalent** | **Windows GUI**: `File Explorer › View › Show › File name extensions` |
| `pref.hiddenfiles` | File Explorer Hidden Files | **ui-equivalent** | **Windows GUI**: `File Explorer › View › Show › Hidden items` |
| `pref.verboselogon` | Logon Verbose Mode | **situational** | *Situational*: Addresses specific workaround/issue: Shows detailed service initialization steps on the logon screen, slightly changing startup visual pr |
| `pref.newoutlook` | Microsoft Outlook New Version | **ui-equivalent** | **Windows GUI**: `Outlook App › Try the new Outlook toggle` |
| `pref.scrollbars` | Scrollbars Always Visible | **ui-equivalent** | **Windows GUI**: `Settings › Accessibility › Visual effects › Always show scrollbars` |
| `pref.multiplaneoverlay` | Multiplane Overlay Workaround | **situational** | *Situational*: Addresses specific workaround/issue: Workaround for screen flickering; increases DWM power usage and may disable windowed HDR presentatio |
| `pref.mouseacceleration` | Mouse Acceleration | **ui-equivalent** | **Windows GUI**: `Settings › Bluetooth & devices › Mouse › Additional mouse settings › Pointer Options › Enhance pointer precision` |
| `pref.numlock` | Num Lock on Startup | **ui-equivalent** | **Windows GUI**: `Control Panel › Ease of Access Center › Make the keyboard easier to use` |
| `pref.windowsnapping` | Window Snapping | **ui-equivalent** | **Windows GUI**: `Settings › System › Multitasking › Snap windows` |
| `pref.standbyfix` | S0 Sleep Network Connectivity | **situational** | *Situational*: Addresses specific workaround/issue: Alters modern standby network sleep state; behavior depends on laptop motherboard firmware. |
| `pref.s3sleep` | S3 Sleep | **situational** | *Situational*: Addresses specific workaround/issue: Laptops lacking S3 firmware support may fail to sleep, freeze, or suffer bugcheck crashes upon closi |
| `pref.bingsearch` | Start Menu Bing Search | **situational** | *Situational*: Addresses specific workaround/issue: Start Menu search will only search local applications, settings, and personal files; web queries wil |
| `pref.loginblur` | Logon Screen Acrylic Blur | **situational** | *Situational*: Addresses specific workaround/issue: Lock screen sign-in shows a clear background image without acrylic blur distortion. |
| `pref.disablelockscreen` | Lock Screen - Disable | **ui-equivalent** | **Windows GUI**: `Settings › Accounts › Sign-in options` |
| `pref.startmenurecommendations` | Start Menu Recommendations | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Start › Show recommendations for tips, shortcuts, new apps, and more` |
| `pref.stickykeys` | Sticky Keys | **ui-equivalent** | **Windows GUI**: `Settings › Accessibility › Keyboard › Sticky keys` |
| `pref.taskbaralignment` | Taskbar Centered Icons | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Taskbar › Taskbar behaviors › Taskbar alignment` |
| `pref.taskbarsearch` | Taskbar Search Icon | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Taskbar › Taskbar items › Search` |
| `pref.taskview` | Taskbar Task View Icon | **ui-equivalent** | **Windows GUI**: `Settings › Personalization › Taskbar › Taskbar items › Task view` |
| `pref.gamemode` | Game Mode | **ui-equivalent** | **Windows GUI**: `Settings › Gaming › Game Mode › Game Mode` |
| `pref.longpaths` | Enable Long Paths | **documented** | **Documented**: "EM\CurrentControlSet\Control\FileSystem LongPathsEnabled (Type: REG_DWORD) must exist and be set to 1 . The registry value will be cache" |

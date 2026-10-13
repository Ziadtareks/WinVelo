# Host System Tweak Detection Report

- **Execution Timestamp**: 2026-10-13 12:17:55 UTC
- **Host OS**: Microsoft Windows NT 10.0.26200.0
- **Machine**: ZIAD
- **Total Tweaks Audited**: 141

| # | Tweak ID | Name | Category | Risk | Status | Error/Exception |
|---|---|---|---|:---:|:---:|---|
| 1 | `advanced.bravedebloat` | Brave Browser - Debloat | system | Low | **Applied** | None |
| 2 | `advanced.disablewarningforunsignedrdp` | RDP Unsigned File Warnings - Disable | system | Low | **Default** | None |
| 3 | `advanced.edgedebloat` | Microsoft Edge - Debloat | system | Low | **Applied** | None |
| 4 | `advanced.removeedge` | Microsoft Edge - Remove | system | High | **Default** | None |
| 5 | `advanced.disablebitlocker` | BitLocker - Disable | system | High | **Default** | None |
| 6 | `advanced.utc` | Date & Time - Set Time to UTC | system | Low | **Applied** | None |
| 7 | `advanced.removeonedrive` | Microsoft OneDrive - Remove | system | High | **Default** | None |
| 8 | `advanced.removehomeandgallery` | File Explorer Home and Gallery - Disable | system | Low | **Default** | None |
| 9 | `advanced.display` | Visual Effects - Set to Best Performance | system | Low | **Default** | None |
| 10 | `advanced.reservedstorage` | Disable Reserved Storage | system | Medium | **Default** | None |
| 11 | `advanced.storage` | Storage Sense - Disable | system | Medium | **Applied** | None |
| 12 | `advanced.windowsai` | Windows AI - Disable And Remove | system | High | **Applied** | None |
| 13 | `advanced.razerblock` | Razer Software Auto-Install - Disable | system | Low | **Applied** | None |
| 14 | `advanced.logiblock` | Logitech Download Assistant Auto-Install - Disable | system | Low | **Default** | None |
| 15 | `advanced.disablenotifications` | System Tray Notifications & Calendar - Disable | system | Medium | **Default** | None |
| 16 | `advanced.rightclickmenu` | Right-Click Menu Previous Layout - Enable | system | Low | **Default** | None |
| 17 | `advanced.ipv46` | IPv6 - Set IPv4 as Preferred | network | Low | **Default** | None |
| 18 | `advanced.teredo` | Teredo - Disable | network | Low | **Default** | None |
| 19 | `advanced.disableipv6` | IPv6 - Disable | network | High | **Default** | None |
| 20 | `advanced.disablebgapps` | Background Apps - Disable | system | Low | **Applied** | None |
| 21 | `essential.activity` | Activity History - Disable | privacy | Safe | **Applied** | None |
| 22 | `essential.hiber` | Hibernation - Disable | system | Safe | **Applied** | None |
| 23 | `essential.widget` | Widgets - Remove | system | Safe | **Default** | None |
| 24 | `essential.revertstartmenu` | Start Menu Previous Layout - Enable | system | Safe | **Default** | None |
| 25 | `essential.disablestoresearch` | Microsoft Store Recommended Search Results - Disable | system | Safe | **Default** | None |
| 26 | `essential.location` | Location Tracking - Disable | privacy | Safe | **Applied** | None |
| 27 | `essential.services` | Services - Set to Manual | performance | Safe | **Default** | None |
| 28 | `essential.consumerfeatures` | ConsumerFeatures - Disable | system | Safe | **Applied** | None |
| 29 | `essential.telemetry` | Telemetry - Disable | privacy | Safe | **Applied** | None |
| 30 | `essential.deliveryoptimization` | Delivery Optimization - Disable | network | Safe | **Applied** | None |
| 31 | `essential.restorepoint` | Restore Point - Create | system | Safe | **Applied** | None |
| 32 | `essential.endtaskontaskbar` | End Task With Right Click - Enable | system | Safe | **Applied** | None |
| 33 | `essential.wpbt` | Windows Platform Binary Table (WPBT) - Disable | system | Safe | **Applied** | None |
| 34 | `essential.preventdevicemetadatafromnetwork` | Prevent Device Companion Apps | system | Safe | **Applied** | None |
| 35 | `essential.diskcleanup` | Disk Cleanup - Run | performance | Safe | **Default** | None |
| 36 | `essential.deletetempfiles` | Temporary Files - Remove | performance | Safe | **Default** | None |
| 37 | `essential.disableexplorerautodiscovery` | File Explorer Automatic Folder Discovery - Disable | system | Safe | **Default** | None |
| 38 | `ai.copilot.disable-taskbar-button` | Hide Copilot Taskbar Button | ai | Safe | **Applied** | None |
| 39 | `ai.copilot.disable-policy` | Disable Windows Copilot Globally | ai | Low | **Applied** | None |
| 40 | `ai.recall.disable-snapshots` | Disable Windows Recall Snapshots | ai | Safe | **Applied** | None |
| 41 | `ai.search.disable-bing-web-search` | Disable Bing Web Search in Start Menu | ai | Safe | **Applied** | None |
| 42 | `gaming.mode.enable` | Enable Windows Game Mode | gaming | Safe | **Applied** | None |
| 43 | `gaming.xbox.disable-game-bar` | Disable Xbox Game Bar Overlay | gaming | Safe | **Applied** | None |
| 44 | `gaming.dvr.disable-background-recording` | Disable Background Game DVR Recording | gaming | Safe | **Applied** | None |
| 45 | `gaming.input.disable-mouse-acceleration` | Disable Pointer Acceleration (Enhance Pointer Precision) | gaming | Safe | **Applied** | None |
| 46 | `network.wifi.disable-hotspot-reporting` | Disable Wi-Fi Sense Hotspot Reporting | network | Safe | **Applied** | None |
| 47 | `network.probing.disable-active-probing` | Disable NCSI Active Internet Probing | network | Medium | **Default** | None |
| 48 | `network.wifi.disable-autoconnect-sense` | Disable Auto-Connect to Suggested Open Wi-Fi | network | Safe | **Applied** | None |
| 49 | `network.optimization.limit-peering-lan` | Restrict Delivery Optimization to Local Network Only | network | Safe | **Default** | None |
| 50 | `network.dns.enable-doh` | Enable DNS over HTTPS (DoH) System Fallback | network | Safe | **Default** | None |
| 51 | `network.teredo.disable-tunneling` | Disable Teredo IPv6 Tunneling | network | Safe | **Applied** | None |
| 52 | `performance.visual.disable-transparency` | Disable Transparency Effects | performance | Safe | **Applied** | None |
| 53 | `performance.visual.disable-animations` | Disable Window Minimize/Maximize Animations | performance | Safe | **Applied** | None |
| 54 | `performance.system.storage-sense` | Enable Storage Sense Auto-Cleanup | performance | Safe | **Default** | None |
| 55 | `performance.network.delivery-optimization-simple` | Disable Delivery Optimization Peer-to-Peer (HTTP Only) | performance | Safe | **Applied** | None |
| 56 | `performance.memory.svchost-split-threshold` | Tune SvcHost Split Threshold | performance | Low | **Default** | None |
| 57 | `performance.system.disable-maintenance-wake` | Disable Automatic Maintenance Wake-Up Timers | performance | Safe | **Default** | None |
| 58 | `performance.search.prevent-indexing-on-battery` | Prevent Search Indexing While on Battery Power | performance | Safe | **Default** | None |
| 59 | `performance.ntfs.disable-last-access` | Disable NTFS Last Access Time Updates | performance | Safe | **Default** | None |
| 60 | `performance.ntfs.disable-8dot3-names` | Disable 8.3 Short File Name Creation | performance | Safe | **Default** | None |
| 61 | `performance.network.disable-remote-differential-compression` | Disable Remote Differential Compression API | performance | Safe | **Default** | None |
| 62 | `privacy.telemetry.diagnostic-data` | Diagnostic Data Level | privacy | Safe | **Applied** | None |
| 63 | `privacy.advertising.advertising-id` | Disable Advertising ID | privacy | Safe | **Applied** | None |
| 64 | `privacy.experiences.tailored-experiences` | Disable Tailored Experiences | privacy | Safe | **Applied** | None |
| 65 | `privacy.activity.activity-history` | Disable Activity History Publishing | privacy | Safe | **Applied** | None |
| 66 | `privacy.speech.online-speech-recognition` | Disable Online Speech Recognition | privacy | Safe | **Default** | None |
| 67 | `privacy.typing.inking-typing-personalization` | Disable Inking & Typing Data Collection | privacy | Safe | **Applied** | None |
| 68 | `privacy.tracking.app-launch-tracking` | Disable App Launch Tracking | privacy | Safe | **Applied** | None |
| 69 | `privacy.feedback.feedback-prompts` | Disable Feedback Frequency Prompts | privacy | Safe | **Applied** | None |
| 70 | `privacy.reporting.windows-error-reporting` | Disable Windows Error Reporting | privacy | Low | **Applied** | None |
| 71 | `privacy.ceip.customer-experience-improvement` | Disable Customer Experience Program (CEIP) | privacy | Safe | **Applied** | None |
| 72 | `privacy.clipboard.cloud-clipboard-sync` | Disable Cloud Clipboard Sync | privacy | Safe | **Applied** | None |
| 73 | `privacy.search.device-search-history` | Disable Device Search History | privacy | Safe | **Applied** | None |
| 74 | `privacy.spotlight.lockscreen-ads` | Disable Lock Screen Tips & Spotlight Ads | privacy | Safe | **Applied** | None |
| 75 | `privacy.suggestions.start-menu-promotions` | Disable Start Menu Suggested App Ads | privacy | Safe | **Applied** | None |
| 76 | `privacy.location.disable-sensor` | Disable System Location Sensor | privacy | Low | **Applied** | None |
| 77 | `privacy.consent.deny-location` | Deny Apps Access to Location by Default | privacy | Safe | **Applied** | None |
| 78 | `privacy.consent.deny-microphone` | Deny UWP Apps Access to Microphone by Default | privacy | Low | **Default** | None |
| 79 | `privacy.consent.deny-webcam` | Deny UWP Apps Access to Webcam by Default | privacy | Low | **Default** | None |
| 80 | `privacy.consent.deny-notifications` | Deny UWP Apps Access to User Notifications | privacy | Safe | **Applied** | None |
| 81 | `privacy.consent.deny-account-info` | Deny Apps Access to Account Information | privacy | Safe | **Applied** | None |
| 82 | `privacy.consent.deny-contacts` | Deny Apps Access to Contacts List | privacy | Safe | **Applied** | None |
| 83 | `privacy.consent.deny-calendar` | Deny Apps Access to User Calendar & Appointments | privacy | Safe | **Applied** | None |
| 84 | `privacy.consent.deny-diagnostics` | Deny Apps Access to Other Apps Diagnostic Info | privacy | Safe | **Applied** | None |
| 85 | `system.security.disable-remote-assistance` | Disable Remote Assistance | system | Safe | **Default** | None |
| 86 | `system.security.disable-autorun` | Disable AutoRun & AutoPlay on All Drives | system | Safe | **Applied** | None |
| 87 | `system.security.disable-wsh` | Disable Windows Script Host (VBS/JS Scripts) | system | Medium | **Default** | None |
| 88 | `system.security.enforce-smartscreen` | Enforce Windows SmartScreen via Policy | system | Safe | **Default** | None |
| 89 | `system.security.disable-llmnr` | Disable Link-Local Multicast Name Resolution (LLMNR) | system | Safe | **Default** | None |
| 90 | `system.security.disable-rdp-auto-enable` | Disable Remote Desktop Inbound Connections | system | Low | **Applied** | None |
| 91 | `system.security.disable-lockscreen-camera` | Disable Camera Access on Lock Screen | system | Safe | **Applied** | None |
| 92 | `ui.explorer.classic-context-menu` | Restore Classic Right-Click Menu | ui | Safe | **Applied** | None |
| 93 | `ui.explorer.show-file-extensions` | Always Show Known File Extensions | ui | Safe | **Applied** | None |
| 94 | `ui.explorer.show-hidden-files` | Show Hidden Files and Folders | ui | Safe | **Applied** | None |
| 95 | `ui.explorer.open-to-this-pc` | Open File Explorer to This PC | ui | Safe | **Applied** | None |
| 96 | `ui.taskbar.end-task-right-click` | Enable 'End Task' in Taskbar Context Menu | ui | Safe | **Applied** | None |
| 97 | `ui.taskbar.show-seconds-clock` | Show Seconds in Taskbar Clock | ui | Safe | **Default** | None |
| 98 | `ui.system.enable-long-paths` | Enable Win32 Long Paths (>260 Chars) | system | Safe | **Default** | None |
| 99 | `ui.taskbar.alignment-left` | Align Taskbar Icons to the Left | ui | Safe | **Default** | None |
| 100 | `ui.explorer.hide-gallery` | Hide Gallery from File Explorer Navigation Pane | ui | Safe | **Default** | None |
| 101 | `ui.explorer.hide-recent-files` | Hide Recent Files in File Explorer Home | ui | Safe | **Default** | None |
| 102 | `ui.explorer.hide-frequent-folders` | Hide Frequently Used Folders in Quick Access | ui | Safe | **Default** | None |
| 103 | `ui.explorer.show-full-path-title` | Display Full Folder Path in File Explorer Title Bar | ui | Safe | **Default** | None |
| 104 | `ui.search.disable-search-highlights` | Disable Search Box Highlights & Illustrations | ui | Safe | **Default** | None |
| 105 | `ui.taskbar.disable-widgets-button` | Hide Widgets Weather Icon from Taskbar | ui | Safe | **Default** | None |
| 106 | `ui.taskbar.disable-chat-button` | Hide Microsoft Teams Chat Icon on Taskbar | ui | Safe | **Applied** | None |
| 107 | `ui.taskbar.disable-task-view-button` | Hide Task View Button from Taskbar | ui | Safe | **Applied** | None |
| 108 | `ui.system.disable-welcome-experience` | Disable Windows Welcome Experience After Updates | ui | Safe | **Applied** | None |
| 109 | `ui.system.disable-tips-suggestions` | Disable Tips, Tricks, and Suggestions About Windows | ui | Safe | **Applied** | None |
| 110 | `ui.logon.hide-fast-user-switching` | Hide Fast User Switching Option | ui | Safe | **Default** | None |
| 111 | `updates.drivers.exclude-from-updates` | Exclude Hardware Drivers from Windows Update | updates | Low | **Applied** | None |
| 112 | `updates.reboot.disable-auto-reboot-logged-on` | Prevent Forced Auto-Reboot While Logged On | updates | Safe | **Default** | None |
| 113 | `updates.au.notify-download` | Notify Before Downloading Windows Updates | updates | Medium | **Default** | None |
| 114 | `updates.peering.metered-connections` | Block Delivery Optimization on Metered Connections | updates | Safe | **Default** | None |
| 115 | `updates.activehours.enable-policy` | Enforce Active Hours Policy Against Mid-Day Restarts | updates | Safe | **Default** | None |
| 116 | `updates.preview.disable-optional-content` | Disable Optional Preview Updates & Beta Rollouts | updates | Safe | **Default** | None |
| 117 | `pref.detailedbsod` | BSoD Verbose Mode | ui | Safe | **Default** | None |
| 118 | `pref.batterypercentage` | System Tray Battery Percentage | ui | Safe | **Default** | None |
| 119 | `pref.darkmode` | Dark Theme for Windows | ui | Safe | **Applied** | None |
| 120 | `pref.showext` | File Explorer File Extensions | ui | Safe | **Applied** | None |
| 121 | `pref.hiddenfiles` | File Explorer Hidden Files | ui | Safe | **Applied** | None |
| 122 | `pref.verboselogon` | Logon Verbose Mode | ui | Safe | **Default** | None |
| 123 | `pref.newoutlook` | Microsoft Outlook New Version | ui | Safe | **Default** | None |
| 124 | `pref.scrollbars` | Scrollbars Always Visible | ui | Safe | **Default** | None |
| 125 | `pref.multiplaneoverlay` | Multiplane Overlay | ui | Safe | **Default** | None |
| 126 | `pref.mouseacceleration` | Mouse Acceleration | ui | Safe | **Default** | None |
| 127 | `pref.numlock` | Num Lock on Startup | ui | Safe | **Default** | None |
| 128 | `pref.windowsnapping` | Window Snapping | ui | Safe | **Applied** | None |
| 129 | `pref.standbyfix` | S0 Sleep Network Connectivity | ui | Safe | **Default** | None |
| 130 | `pref.s3sleep` | S3 Sleep | ui | Safe | **Default** | None |
| 131 | `pref.hidesettingshome` | Settings Home Page | ui | Safe | **Default** | None |
| 132 | `pref.bingsearch` | Start Menu Bing Search | ui | Safe | **Default** | None |
| 133 | `pref.loginblur` | Logon Screen Acrylic Blur | ui | Safe | **Default** | None |
| 134 | `pref.disablelockscreen` | Lock Screen - Disable | ui | Safe | **Default** | None |
| 135 | `pref.startmenurecommendations` | Start Menu Recommendations | ui | Safe | **Default** | None |
| 136 | `pref.stickykeys` | Sticky Keys | ui | Safe | **Default** | None |
| 137 | `pref.taskbaralignment` | Taskbar Centered Icons | ui | Safe | **Applied** | None |
| 138 | `pref.taskbarsearch` | Taskbar Search Icon | ui | Safe | **Default** | None |
| 139 | `pref.taskview` | Taskbar Task View Icon | ui | Safe | **Default** | None |
| 140 | `pref.gamemode` | Game Mode | ui | Safe | **Default** | None |
| 141 | `pref.longpaths` | Enable Long Paths | ui | Safe | **Default** | None |

## Summary

- **Applied**: 69
- **Default (Inactive)**: 72
- **Errors/Access Issues**: 0
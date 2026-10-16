# Tweakora Topic Tags Audit

This document provides a complete audit of semantic topic tags assigned to all catalog tweaks in WinVelo (Tweakora).
Tags are assigned based on what each tweak actually does (its apply actions and functional effects), not from name alone.

Allowed topic tags: `gaming`, `privacy`, `ai`, `ui`, `performance`, `network`, `updates`, `apps`.

Total catalog tweaks audited: **137**.

| Tweak ID | Name | Tags Assigned | Rationale (Actions & whatItDoes) |
| :--- | :--- | :--- | :--- |
| `advanced.bravedebloat` | Brave Browser - Debloat | `apps, privacy, ai` | Disables Brave browser background telemetry, wallet, and AI chat features. |
| `advanced.disablebgapps` | Background Apps - Disable | `apps, performance, privacy` | Prevents Windows Store and UWP applications from running and syncing in the background. |
| `advanced.disablebitlocker` | BitLocker - Disable | `performance` | Disables BitLocker volume encryption to eliminate encryption overhead on disk I/O. |
| `advanced.disableipv6` | IPv6 - Disable | `network` | Completely disables the IPv6 networking stack on all network adapters. |
| `advanced.disablenotifications` | System Tray Notifications & Calendar - Disable | `ui, gaming` | Suppresses all system tray toast notifications and Action Center popups to prevent interruptions. |
| `advanced.disablewarningforunsignedrdp` | RDP Unsigned File Warnings - Disable | `network, ui` | Suppresses security warning prompts when initiating Remote Desktop network sessions. |
| `advanced.display` | Visual Effects - Set to Best Performance | `performance, ui` | Disables window animations and desktop visual effects for maximum shell responsiveness. |
| `advanced.edgedebloat` | Microsoft Edge - Debloat | `apps, privacy, ui` | Disables promotional telemetry, background shopping tools, and widgets in Edge browser. |
| `advanced.ipv46` | IPv6 - Set IPv4 as Preferred | `network` | Configures the TCP/IP stack to prioritize IPv4 address resolution over IPv6. |
| `advanced.logiblock` | Logitech Download Assistant Auto-Install - Disable | `apps, gaming` | Blocks Logitech Download Assistant peripheral auto-installer from launching at startup. |
| `advanced.razerblock` | Razer Software Auto-Install - Disable | `apps, gaming` | Prevents automatic background installation prompts for Razer peripheral companion software. |
| `advanced.removeedge` | Microsoft Edge - Remove | `apps` | Removes the Microsoft Edge browser application package and prevents reinstallation. |
| `advanced.removehomeandgallery` | File Explorer Home and Gallery - Disable | `ui` | Removes Home and Gallery namespace entries from the File Explorer navigation tree. |
| `advanced.removeonedrive` | Microsoft OneDrive - Remove | `apps, performance, privacy` | Uninstalls OneDrive client to eliminate background synchronization overhead and file telemetry. |
| `advanced.reservedstorage` | Disable Reserved Storage | `performance, updates` | Reclaims 7GB of disk space reserved for staging Windows Updates. |
| `advanced.teredo` | Teredo - Disable | `network` | Disables Teredo tunneling adapter to eliminate obsolete IPv6-over-IPv4 transition traffic. |
| `advanced.utc` | Date & Time - Set Time to UTC | `ui` | Configures hardware clock to UTC to synchronize system time displays across OSes. |
| `advanced.windowsai` | Windows AI - Disable And Remove | `ai, privacy, ui` | Disables system-wide generative AI features, Recall snapshots, and Copilot UI integrations. |
| `ai.copilot.disable-policy` | Disable Windows Copilot Globally | `ai, privacy, ui` | Disables the Windows Copilot assistant machine-wide via Group Policy. |
| `ai.copilot.disable-taskbar-button` | Hide Copilot Taskbar Button | `ai, ui` | Hides the Copilot artificial intelligence icon from the Windows taskbar. |
| `ai.recall.disable-snapshots` | Disable Windows Recall Snapshots | `ai, privacy` | Disables automated snapshot capture and background OCR indexing in Windows Recall. |
| `ai.search.disable-bing-web-search` | Disable Bing Web Search in Start Menu | `ai, privacy, ui, network` | Stops sending Start Menu search queries to Bing web services and AI suggestion engines. |
| `essential.activity` | Activity History - Disable | `privacy` | Stops tracking user application activity history and syncing it to the cloud. |
| `essential.consumerfeatures` | ConsumerFeatures - Disable | `apps, privacy, ui` | Blocks Windows from silently pre-installing sponsored third-party apps and games. |
| `essential.deletetempfiles` | Temporary Files - Remove | `performance` | Purges user and system temporary folders to reclaim storage space. |
| `essential.disableexplorerautodiscovery` | File Explorer Automatic Folder Discovery - Disable | `network, performance, ui` | Disables local network UPnP/SSDP device discovery in File Explorer to speed up folder browsing. |
| `essential.disablestoresearch` | Microsoft Store Recommended Search Results - Disable | `apps, ui, privacy` | Disables promotional store app recommendations injected into search results. |
| `essential.diskcleanup` | Disk Cleanup - Run | `performance` | Automates disk cleanup to purge accumulated temporary files and obsolete update caches. |
| `essential.hiber` | Hibernation - Disable | `performance` | Disables system hibernation file to free gigabytes of drive storage. |
| `essential.location` | Location Tracking - Disable | `privacy` | Shuts down location sensor platform and disables application location tracking services. |
| `essential.preventdevicemetadatafromnetwork` | Prevent Device Companion Apps | `network, privacy` | Prevents background network requests to download device manufacturer metadata and icons. |
| `essential.restorepoint` | Restore Point - Create | `performance` | Configures system restore frequency and generates a pre-tweak snapshot checkpoint. |
| `essential.revertstartmenu` | Start Menu Previous Layout - Enable | `ui` | Restores previous Windows Start Menu layout style. |
| `essential.services` | Services - Set to Manual | `performance, privacy` | Sets unnecessary telemetry and sync services to manual/disabled to free background CPU and RAM. |
| `essential.telemetry` | Telemetry - Disable | `privacy, performance` | Restricts diagnostic telemetry data collection, advertising identifiers, and inking tracking. |
| `essential.widget` | Widgets - Remove | `apps, ui, performance` | Uninstalls WebExperience widget host and removes the News & Weather widget from the taskbar. |
| `essential.wpbt` | Windows Platform Binary Table (WPBT) - Disable | `performance, privacy` | Prevents UEFI firmware from automatically injecting vendor-bundled executables into the OS. |
| `gaming.dvr.disable-background-recording` | Disable Background Game DVR Recording | `gaming, performance` | Disables rolling background gameplay video recording to eliminate disk and GPU overhead. |
| `gaming.input.disable-mouse-acceleration` | Disable Mouse Acceleration (Enhance Pointer Precision) | `gaming, ui` | Disables pointer precision enhancement to provide 1:1 raw linear mouse input for gaming. |
| `gaming.mode.enable` | Enable Windows Game Mode | `gaming, performance` | Activates Windows Game Mode to prioritize hardware resources for active gaming processes. |
| `gaming.xbox.disable-game-bar` | Disable Xbox Game Bar Overlay | `gaming, performance, ui` | Disables the Xbox Game Bar background overlay and capture services. |
| `network.dns.enable-doh` | Enable DNS over HTTPS (DoH) System Fallback | `network, privacy` | Enables DNS over HTTPS (DoH) encrypted queries with standard fallback. |
| `network.optimization.limit-peering-lan` | Restrict Delivery Optimization to Local Network Only | `network, updates` | Restricts Delivery Optimization update peering strictly to computers on the local subnet. |
| `network.probing.disable-active-probing` | Disable NCSI Active Internet Probing | `network, privacy` | Disables periodic HTTP probes to msftconnecttest.com for internet reachability checks. |
| `network.teredo.disable-tunneling` | Disable Teredo IPv6 Tunneling | `network` | Disables Teredo tunneling transition protocol to prevent unwanted IPv6 encapsulated traffic. |
| `network.wifi.disable-autoconnect-sense` | Disable Auto-Connect to Suggested Open Wi-Fi | `network, privacy` | Disables automatic connection to open public Wi-Fi hotspots recommended by Wi-Fi Sense. |
| `network.wifi.disable-hotspot-reporting` | Disable Wi-Fi Sense Hotspot Reporting | `network, privacy` | Prevents automatic reporting of available open Wi-Fi network locations to Microsoft. |
| `performance.cpu.priority-separation` | Adjust Win32 Priority Separation | `performance, gaming` | Allocates longer CPU scheduling quanta to foreground games and interactive apps. |
| `performance.cpu.unpark-cores` | Disable CPU Core Parking (Experimental) | `performance, gaming` | Prevents CPU cores from parking to avoid micro-stutter when CPU loads fluctuate. |
| `performance.gaming.mmcss-responsiveness` | Multimedia Class Scheduler Service (MMCSS) Gaming Priority | `gaming, performance, network` | Boosts MMCSS thread priority and disables network throttling during gameplay. |
| `performance.gpu.disable-mpo` | Disable Multi-Plane Overlay (MPO Workaround) | `gaming, performance, ui` | Disables Multi-Plane Overlay to resolve display stuttering and black-screen glitches in games. |
| `performance.gpu.hags` | Hardware-Accelerated GPU Scheduling (HAGS) | `gaming, performance` | Enables hardware-accelerated GPU scheduling to reduce frame latency in modern games. |
| `performance.input.buffer-queue` | Mouse & Keyboard Buffer Queue (Experimental) | `gaming, performance` | Reduces mouse and keyboard input buffer queue size for lower input processing latency. |
| `performance.memory.svchost-split-threshold` | Tune SvcHost Split Threshold | `performance` | Tunes service host memory thresholds to group svchost processes and lower memory footprint. |
| `performance.network.delivery-optimization-simple` | Disable Delivery Optimization Peer-to-Peer (HTTP Only) | `network, performance, updates` | Configures Delivery Optimization to simple HTTP download mode, disabling peer uploads. |
| `performance.network.disable-remote-differential-compression` | Disable Remote Differential Compression API | `network, performance` | Disables Remote Differential Compression network algorithms to improve LAN transfer speeds. |
| `performance.ntfs.disable-8dot3-names` | Disable 8.3 Short File Name Creation | `performance` | Disables legacy 8.3 short filename generation to speed up NTFS directory operations. |
| `performance.ntfs.disable-last-access` | Disable NTFS Last Access Time Updates | `performance` | Disables NTFS last access timestamp updates on files to reduce disk write activity. |
| `performance.power.disable-throttling` | Disable Windows Power Throttling | `performance, gaming` | Disables Windows Power Throttling to keep background threads operating at full frequency. |
| `performance.search.prevent-indexing-on-battery` | Prevent Search Indexing While on Battery Power | `performance` | Suspends Windows Search file indexing while operating on battery power. |
| `performance.system.disable-maintenance-wake` | Disable Automatic Maintenance Wake-Up Timers | `performance` | Prevents scheduled system maintenance tasks from waking the computer from sleep. |
| `performance.system.storage-sense` | Enable Storage Sense Auto-Cleanup | `performance` | Enables automated Windows Storage Sense background cleanup for temporary files. |
| `performance.visual.disable-animations` | Disable Window Minimize/Maximize Animations | `performance, ui` | Disables window minimize, maximize, and taskbar animation transitions. |
| `performance.visual.disable-transparency` | Disable Transparency Effects | `performance, ui` | Disables GPU-rendered acrylic transparency across taskbar and application backgrounds. |
| `pref.batterypercentage` | System Tray Battery Percentage | `ui` | Displays exact remaining battery percentage directly on the taskbar system tray. |
| `pref.bingsearch` | Start Menu Bing Search | `ui, privacy, network` | Removes web search suggestions and Bing integration from the Windows Start Menu search box. |
| `pref.darkmode` | Dark Theme for Windows | `ui` | Applies dark color palette across Windows shell, taskbar, and modern applications. |
| `pref.detailedbsod` | BSoD Verbose Mode | `ui` | Replaces standard simplified crash screen with detailed technical stop error codes. |
| `pref.disablelockscreen` | Lock Screen - Disable | `ui` | Bypasses the initial lock screen image to display the sign-in password prompt directly. |
| `pref.hidesettingshome` | Settings Home Page | `ui, privacy` | Hides the recommended content and promotional cards page in Windows 11 Settings. |
| `pref.loginblur` | Logon Screen Acrylic Blur | `ui, performance` | Removes blur rendering on the login screen for faster transition and clear background image. |
| `pref.multiplaneoverlay` | Multiplane Overlay Workaround | `gaming, performance, ui` | Workaround for GPU stutter and desktop flickering by disabling Multi-Plane Overlay. |
| `pref.newoutlook` | Microsoft Outlook New Version | `apps, ui` | Prevents automatic forced migration from classic Outlook to the web-based New Outlook app. |
| `pref.numlock` | Num Lock on Startup | `ui` | Automatically activates the numeric keypad NumLock key during boot and logon. |
| `pref.s3sleep` | S3 Sleep | `performance` | Forces traditional S3 sleep state to prevent modern standby overheating and power loss. |
| `pref.scrollbars` | Scrollbars Always Visible | `ui` | Keeps window scrollbars persistently expanded and visible without requiring hover. |
| `pref.standbyfix` | S0 Sleep Network Connectivity | `network, performance` | Disables network connectivity during modern standby sleep to prevent battery drain. |
| `pref.startmenurecommendations` | Start Menu Recommendations | `ui, privacy` | Collapses and hides the Recommended files and recent items section from the Start Menu. |
| `pref.stickykeys` | Sticky Keys | `gaming, ui` | Prevents the 5x Shift key shortcut from interrupting fullscreen games and apps. |
| `pref.taskbarsearch` | Taskbar Search Icon | `ui` | Customizes the taskbar search element size and appearance. |
| `pref.verboselogon` | Logon Verbose Mode | `ui` | Displays detailed progress messages during system startup, login, and shutdown. |
| `pref.windowsnapping` | Window Snapping | `ui` | Enables and configures window edge snapping and layout arrangement behaviors. |
| `privacy.activity.activity-history` | Disable Activity History Publishing | `privacy` | Disables publishing and cloud uploading of user application activity history. |
| `privacy.advertising.advertising-id` | Disable Advertising ID | `privacy` | Disables unique advertising identifiers used for targeted cross-application tracking. |
| `privacy.ceip.customer-experience-improvement` | Disable Customer Experience Program (CEIP) | `privacy` | Disables participation in the Customer Experience Improvement Program (CEIP). |
| `privacy.clipboard.cloud-clipboard-sync` | Disable Cloud Clipboard Sync | `privacy` | Prevents clipboard copy history from synchronizing across multiple Windows devices. |
| `privacy.consent.deny-account-info` | Deny Apps Access to Account Information | `privacy, apps` | Configures default application permission to deny access to account profile details. |
| `privacy.consent.deny-calendar` | Deny Apps Access to User Calendar & Appointments | `privacy, apps` | Configures default application permission to deny access to user calendar appointments. |
| `privacy.consent.deny-contacts` | Deny Apps Access to Contacts List | `privacy, apps` | Configures default application permission to deny access to address book contacts. |
| `privacy.consent.deny-diagnostics` | Deny Apps Access to Other Apps Diagnostic Info | `privacy, apps` | Configures default application permission to deny access to diagnostic information from other apps. |
| `privacy.consent.deny-location` | Deny Apps Access to Location by Default | `privacy, apps` | Configures default application permission to deny access to system location. |
| `privacy.consent.deny-microphone` | Deny UWP Apps Access to Microphone by Default | `privacy, apps` | Configures default application permission to deny access to microphone audio. |
| `privacy.consent.deny-notifications` | Deny UWP Apps Access to User Notifications | `privacy, apps, ui` | Configures default application permission to deny access to user notifications. |
| `privacy.consent.deny-webcam` | Deny UWP Apps Access to Webcam by Default | `privacy, apps` | Configures default application permission to deny access to cameras and webcams. |
| `privacy.experiences.tailored-experiences` | Disable Tailored Experiences | `privacy` | Prevents Microsoft from offering tailored recommendations based on user diagnostic data. |
| `privacy.feedback.feedback-prompts` | Disable Feedback Frequency Prompts | `privacy, ui` | Sets Windows feedback frequency to 'Never', suppressing survey prompts. |
| `privacy.location.disable-sensor` | Disable System Location Sensor | `privacy` | Disables the system-wide location sensor platform and hardware sensors. |
| `privacy.reporting.windows-error-reporting` | Disable Windows Error Reporting | `privacy` | Disables automated Windows Error Reporting crash dump generation and upload. |
| `privacy.search.device-search-history` | Disable Device Search History | `privacy, ui` | Disables recording of local search query history on the device. |
| `privacy.speech.online-speech-recognition` | Disable Online Speech Recognition | `privacy` | Disables cloud-based speech recognition and audio sample collection. |
| `privacy.spotlight.lockscreen-ads` | Disable Lock Screen Tips & Spotlight Ads | `privacy, ui` | Disables promotional suggestions, tips, and Spotlight advertisements on the lock screen. |
| `privacy.suggestions.start-menu-promotions` | Disable Start Menu Suggested App Ads | `privacy, ui, apps` | Disables suggested app promotions and sponsored content in the Start Menu. |
| `privacy.telemetry.diagnostic-data` | Diagnostic Data Level | `privacy` | Minimizes diagnostic data collection to Security/Basic level. |
| `privacy.tracking.app-launch-tracking` | Disable App Launch Tracking | `privacy, ui` | Disables tracking of application launch frequency used for Start menu personalization. |
| `privacy.typing.inking-typing-personalization` | Disable Inking & Typing Data Collection | `privacy` | Disables harvesting of handwritten ink samples and typed keystroke dictionary data. |
| `system.security.disable-autorun` | Disable AutoRun & AutoPlay on All Drives | `ui` | Disables AutoRun and AutoPlay execution across all removable and fixed drives. |
| `system.security.disable-llmnr` | Disable Link-Local Multicast Name Resolution (LLMNR) | `network` | Disables Link-Local Multicast Name Resolution (LLMNR) to prevent local broadcast poisoning. |
| `system.security.disable-lockscreen-camera` | Disable Camera Access on Lock Screen | `privacy, ui` | Disables camera capture access directly from the Windows lock screen without unlocking. |
| `system.security.disable-rdp-auto-enable` | Disable Remote Desktop Inbound Connections | `network` | Enforces denial of inbound Remote Desktop connections to prevent unauthorized remote access. |
| `system.security.disable-remote-assistance` | Disable Remote Assistance | `network, privacy` | Disables Windows Remote Assistance to block incoming remote help requests. |
| `system.security.disable-wsh` | Disable Windows Script Host (VBS/JS Scripts) | `apps` | Disables Windows Script Host (WSH) engine execution for VBScript and JScript files. |
| `system.security.enforce-smartscreen` | Enforce Windows SmartScreen via Policy | `apps, network` | Enforces Windows Defender SmartScreen file and website reputation checks. |
| `ui.explorer.classic-context-menu` | Restore Classic Right-Click Menu | `ui` | Restores classic Windows 10 full right-click context menu in Windows 11 File Explorer. |
| `ui.explorer.hide-frequent-folders` | Hide Frequently Used Folders in Quick Access | `ui, privacy` | Hides frequently used folders from Quick Access and Home in File Explorer. |
| `ui.explorer.hide-gallery` | Hide Gallery from File Explorer Navigation Pane | `ui` | Hides the Gallery folder entry from the File Explorer navigation pane. |
| `ui.explorer.hide-recent-files` | Hide Recent Files in File Explorer Home | `ui, privacy` | Hides recently opened files from Quick Access and Home in File Explorer. |
| `ui.explorer.open-to-this-pc` | Open File Explorer to This PC | `ui` | Sets File Explorer to open to 'This PC' by default instead of Home/Quick Access. |
| `ui.explorer.show-file-extensions` | Always Show Known File Extensions | `ui` | Always displays file extensions for known file types in File Explorer. |
| `ui.explorer.show-full-path-title` | Display Full Folder Path in File Explorer Title Bar | `ui` | Displays the complete folder file system path in the File Explorer title bar. |
| `ui.explorer.show-hidden-files` | Show Hidden Files and Folders | `ui` | Configures File Explorer to make hidden files and folders visible. |
| `ui.logon.hide-fast-user-switching` | Hide Fast User Switching Option | `ui` | Hides the Fast User Switching button from the sign-in screen and Start menu. |
| `ui.search.disable-search-highlights` | Disable Search Box Highlights & Illustrations | `ui, privacy` | Disables decorative search highlights and doodles inside the taskbar search box. |
| `ui.system.disable-tips-suggestions` | Disable Tips, Tricks, and Suggestions About Windows | `ui` | Disables tips, tricks, and operating system feature suggestions in Windows notifications. |
| `ui.system.disable-welcome-experience` | Disable Windows Welcome Experience After Updates | `ui` | Suppresses the full-screen Windows Welcome Experience displayed after feature updates. |
| `ui.system.enable-long-paths` | Enable Win32 Long Paths (>260 Chars) | `ui, apps` | Removes 260-character path length limitations for modern Win32 and .NET applications. |
| `ui.taskbar.alignment-left` | Align Taskbar Icons to the Left | `ui` | Aligns Windows 11 taskbar icons to the left side like traditional Windows layouts. |
| `ui.taskbar.disable-chat-button` | Hide Microsoft Teams Chat Icon on Taskbar | `ui` | Hides the Microsoft Teams Chat button from the Windows 11 taskbar. |
| `ui.taskbar.disable-task-view-button` | Hide Task View Button from Taskbar | `ui` | Hides the Task View desktop switcher button from the Windows 11 taskbar. |
| `ui.taskbar.disable-widgets-button` | Hide Widgets Weather Icon from Taskbar | `ui, performance` | Hides the Widgets weather icon button from the Windows 11 taskbar. |
| `ui.taskbar.end-task-right-click` | Enable 'End Task' in Taskbar Context Menu | `ui, performance` | Adds an immediate 'End Task' action to taskbar application right-click menus. |
| `ui.taskbar.show-seconds-clock` | Show Seconds in Taskbar Clock | `ui` | Displays seconds on the taskbar system tray clock. |
| `updates.activehours.enable-policy` | Enforce Active Hours Policy Against Mid-Day Restarts | `updates` | Enforces configured Active Hours range during which automatic restart will never occur. |
| `updates.au.notify-download` | Notify Before Downloading Windows Updates | `updates` | Configures Automatic Updates to notify before downloading and installing updates. |
| `updates.drivers.exclude-from-updates` | Exclude Hardware Drivers from Windows Update | `updates` | Prevents Windows Update from automatically overwriting dedicated hardware drivers. |
| `updates.peering.metered-connections` | Block Delivery Optimization on Metered Connections | `updates, network` | Blocks Delivery Optimization peer sharing when connected to metered or cellular networks. |
| `updates.preview.disable-optional-content` | Disable Optional Preview Updates & Beta Rollouts | `updates` | Disables automatic installation of preview and optional non-security update rollouts. |
| `updates.reboot.disable-auto-reboot-logged-on` | Prevent Forced Auto-Reboot While Logged On | `updates` | Prevents Windows Update from automatically rebooting the computer while a user is signed in. |

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TagApplier;

public static class Program
{
    private sealed record TweakTagInfo(string[] Tags, string Rationale);

    private static readonly Dictionary<string, TweakTagInfo> TagMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Advanced Tweaks (18)
        ["advanced.bravedebloat"] = new(["apps", "privacy", "ai"], "Disables Brave browser background telemetry, wallet, and AI chat features."),
        ["advanced.disablewarningforunsignedrdp"] = new(["network", "ui"], "Suppresses security warning prompts when initiating Remote Desktop network sessions."),
        ["advanced.edgedebloat"] = new(["apps", "privacy", "ui"], "Disables promotional telemetry, background shopping tools, and widgets in Edge browser."),
        ["advanced.removeedge"] = new(["apps"], "Removes the Microsoft Edge browser application package and prevents reinstallation."),
        ["advanced.disablebitlocker"] = new(["performance"], "Disables BitLocker volume encryption to eliminate encryption overhead on disk I/O."),
        ["advanced.utc"] = new(["ui"], "Configures hardware clock to UTC to synchronize system time displays across OSes."),
        ["advanced.removeonedrive"] = new(["apps", "performance", "privacy"], "Uninstalls OneDrive client to eliminate background synchronization overhead and file telemetry."),
        ["advanced.removehomeandgallery"] = new(["ui"], "Removes Home and Gallery namespace entries from the File Explorer navigation tree."),
        ["advanced.display"] = new(["performance", "ui"], "Disables window animations and desktop visual effects for maximum shell responsiveness."),
        ["advanced.reservedstorage"] = new(["performance", "updates"], "Reclaims 7GB of disk space reserved for staging Windows Updates."),
        ["advanced.windowsai"] = new(["ai", "privacy", "ui"], "Disables system-wide generative AI features, Recall snapshots, and Copilot UI integrations."),
        ["advanced.razerblock"] = new(["apps", "gaming"], "Prevents automatic background installation prompts for Razer peripheral companion software."),
        ["advanced.logiblock"] = new(["apps", "gaming"], "Blocks Logitech Download Assistant peripheral auto-installer from launching at startup."),
        ["advanced.disablenotifications"] = new(["ui", "gaming"], "Suppresses all system tray toast notifications and Action Center popups to prevent interruptions."),
        ["advanced.ipv46"] = new(["network"], "Configures the TCP/IP stack to prioritize IPv4 address resolution over IPv6."),
        ["advanced.teredo"] = new(["network"], "Disables Teredo tunneling adapter to eliminate obsolete IPv6-over-IPv4 transition traffic."),
        ["advanced.disableipv6"] = new(["network"], "Completely disables the IPv6 networking stack on all network adapters."),
        ["advanced.disablebgapps"] = new(["apps", "performance", "privacy"], "Prevents Windows Store and UWP applications from running and syncing in the background."),

        // Essential Tweaks (15)
        ["essential.activity"] = new(["privacy"], "Stops tracking user application activity history and syncing it to the cloud."),
        ["essential.hiber"] = new(["performance"], "Disables system hibernation file to free gigabytes of drive storage."),
        ["essential.widget"] = new(["apps", "ui", "performance"], "Uninstalls WebExperience widget host and removes the News & Weather widget from the taskbar."),
        ["essential.revertstartmenu"] = new(["ui"], "Restores previous Windows Start Menu layout style."),
        ["essential.disablestoresearch"] = new(["apps", "ui", "privacy"], "Disables promotional store app recommendations injected into search results."),
        ["essential.location"] = new(["privacy"], "Shuts down location sensor platform and disables application location tracking services."),
        ["essential.services"] = new(["performance", "privacy"], "Sets unnecessary telemetry and sync services to manual/disabled to free background CPU and RAM."),
        ["essential.consumerfeatures"] = new(["apps", "privacy", "ui"], "Blocks Windows from silently pre-installing sponsored third-party apps and games."),
        ["essential.telemetry"] = new(["privacy", "performance"], "Restricts diagnostic telemetry data collection, advertising identifiers, and inking tracking."),
        ["essential.restorepoint"] = new(["performance"], "Configures system restore frequency and generates a pre-tweak snapshot checkpoint."),
        ["essential.wpbt"] = new(["performance", "privacy"], "Prevents UEFI firmware from automatically injecting vendor-bundled executables into the OS."),
        ["essential.preventdevicemetadatafromnetwork"] = new(["network", "privacy"], "Prevents background network requests to download device manufacturer metadata and icons."),
        ["essential.diskcleanup"] = new(["performance"], "Automates disk cleanup to purge accumulated temporary files and obsolete update caches."),
        ["essential.deletetempfiles"] = new(["performance"], "Purges user and system temporary folders to reclaim storage space."),
        ["essential.disableexplorerautodiscovery"] = new(["network", "performance", "ui"], "Disables local network UPnP/SSDP device discovery in File Explorer to speed up folder browsing."),

        // Performance Tweaks (7)
        ["performance.cpu.priority-separation"] = new(["performance", "gaming"], "Allocates longer CPU scheduling quanta to foreground games and interactive apps."),
        ["performance.gaming.mmcss-responsiveness"] = new(["gaming", "performance", "network"], "Boosts MMCSS thread priority and disables network throttling during gameplay."),
        ["performance.gpu.hags"] = new(["gaming", "performance"], "Enables hardware-accelerated GPU scheduling to reduce frame latency in modern games."),
        ["performance.gpu.disable-mpo"] = new(["gaming", "performance", "ui"], "Disables Multi-Plane Overlay to resolve display stuttering and black-screen glitches in games."),
        ["performance.input.buffer-queue"] = new(["gaming", "performance"], "Reduces mouse and keyboard input buffer queue size for lower input processing latency."),
        ["performance.cpu.unpark-cores"] = new(["performance", "gaming"], "Prevents CPU cores from parking to avoid micro-stutter when CPU loads fluctuate."),
        ["performance.power.disable-throttling"] = new(["performance", "gaming"], "Disables Windows Power Throttling to keep background threads operating at full frequency."),

        // Preferences Tweaks (18)
        ["pref.detailedbsod"] = new(["ui"], "Replaces standard simplified crash screen with detailed technical stop error codes."),
        ["pref.batterypercentage"] = new(["ui"], "Displays exact remaining battery percentage directly on the taskbar system tray."),
        ["pref.darkmode"] = new(["ui"], "Applies dark color palette across Windows shell, taskbar, and modern applications."),
        ["pref.verboselogon"] = new(["ui"], "Displays detailed progress messages during system startup, login, and shutdown."),
        ["pref.newoutlook"] = new(["apps", "ui"], "Prevents automatic forced migration from classic Outlook to the web-based New Outlook app."),
        ["pref.scrollbars"] = new(["ui"], "Keeps window scrollbars persistently expanded and visible without requiring hover."),
        ["pref.multiplaneoverlay"] = new(["gaming", "performance", "ui"], "Workaround for GPU stutter and desktop flickering by disabling Multi-Plane Overlay."),
        ["pref.numlock"] = new(["ui"], "Automatically activates the numeric keypad NumLock key during boot and logon."),
        ["pref.windowsnapping"] = new(["ui"], "Enables and configures window edge snapping and layout arrangement behaviors."),
        ["pref.standbyfix"] = new(["network", "performance"], "Disables network connectivity during modern standby sleep to prevent battery drain."),
        ["pref.s3sleep"] = new(["performance"], "Forces traditional S3 sleep state to prevent modern standby overheating and power loss."),
        ["pref.hidesettingshome"] = new(["ui", "privacy"], "Hides the recommended content and promotional cards page in Windows 11 Settings."),
        ["pref.bingsearch"] = new(["ui", "privacy", "network"], "Removes web search suggestions and Bing integration from the Windows Start Menu search box."),
        ["pref.loginblur"] = new(["ui", "performance"], "Removes blur rendering on the login screen for faster transition and clear background image."),
        ["pref.disablelockscreen"] = new(["ui"], "Bypasses the initial lock screen image to display the sign-in password prompt directly."),
        ["pref.startmenurecommendations"] = new(["ui", "privacy"], "Collapses and hides the Recommended files and recent items section from the Start Menu."),
        ["pref.stickykeys"] = new(["gaming", "ui"], "Prevents the 5x Shift key shortcut from interrupting fullscreen games and apps."),
        ["pref.taskbarsearch"] = new(["ui"], "Customizes the taskbar search element size and appearance."),

        // Extras Tweaks (79)
        ["ai.copilot.disable-taskbar-button"] = new(["ai", "ui"], "Hides the Copilot artificial intelligence icon from the Windows taskbar."),
        ["ai.copilot.disable-policy"] = new(["ai", "privacy", "ui"], "Disables the Windows Copilot assistant machine-wide via Group Policy."),
        ["ai.recall.disable-snapshots"] = new(["ai", "privacy"], "Disables automated snapshot capture and background OCR indexing in Windows Recall."),
        ["ai.search.disable-bing-web-search"] = new(["ai", "privacy", "ui", "network"], "Stops sending Start Menu search queries to Bing web services and AI suggestion engines."),
        ["gaming.mode.enable"] = new(["gaming", "performance"], "Activates Windows Game Mode to prioritize hardware resources for active gaming processes."),
        ["gaming.xbox.disable-game-bar"] = new(["gaming", "performance", "ui"], "Disables the Xbox Game Bar background overlay and capture services."),
        ["gaming.dvr.disable-background-recording"] = new(["gaming", "performance"], "Disables rolling background gameplay video recording to eliminate disk and GPU overhead."),
        ["gaming.input.disable-mouse-acceleration"] = new(["gaming", "ui"], "Disables pointer precision enhancement to provide 1:1 raw linear mouse input for gaming."),
        ["network.wifi.disable-hotspot-reporting"] = new(["network", "privacy"], "Prevents automatic reporting of available open Wi-Fi network locations to Microsoft."),
        ["network.probing.disable-active-probing"] = new(["network", "privacy"], "Disables periodic HTTP probes to msftconnecttest.com for internet reachability checks."),
        ["network.wifi.disable-autoconnect-sense"] = new(["network", "privacy"], "Disables automatic connection to open public Wi-Fi hotspots recommended by Wi-Fi Sense."),
        ["network.optimization.limit-peering-lan"] = new(["network", "updates"], "Restricts Delivery Optimization update peering strictly to computers on the local subnet."),
        ["network.dns.enable-doh"] = new(["network", "privacy"], "Enables DNS over HTTPS (DoH) encrypted queries with standard fallback."),
        ["network.teredo.disable-tunneling"] = new(["network"], "Disables Teredo tunneling transition protocol to prevent unwanted IPv6 encapsulated traffic."),
        ["performance.visual.disable-transparency"] = new(["performance", "ui"], "Disables GPU-rendered acrylic transparency across taskbar and application backgrounds."),
        ["performance.visual.disable-animations"] = new(["performance", "ui"], "Disables window minimize, maximize, and taskbar animation transitions."),
        ["performance.system.storage-sense"] = new(["performance"], "Enables automated Windows Storage Sense background cleanup for temporary files."),
        ["performance.network.delivery-optimization-simple"] = new(["network", "performance", "updates"], "Configures Delivery Optimization to simple HTTP download mode, disabling peer uploads."),
        ["performance.memory.svchost-split-threshold"] = new(["performance"], "Tunes service host memory thresholds to group svchost processes and lower memory footprint."),
        ["performance.system.disable-maintenance-wake"] = new(["performance"], "Prevents scheduled system maintenance tasks from waking the computer from sleep."),
        ["performance.search.prevent-indexing-on-battery"] = new(["performance"], "Suspends Windows Search file indexing while operating on battery power."),
        ["performance.ntfs.disable-last-access"] = new(["performance"], "Disables NTFS last access timestamp updates on files to reduce disk write activity."),
        ["performance.ntfs.disable-8dot3-names"] = new(["performance"], "Disables legacy 8.3 short filename generation to speed up NTFS directory operations."),
        ["performance.network.disable-remote-differential-compression"] = new(["network", "performance"], "Disables Remote Differential Compression network algorithms to improve LAN transfer speeds."),
        ["privacy.telemetry.diagnostic-data"] = new(["privacy"], "Minimizes diagnostic data collection to Security/Basic level."),
        ["privacy.advertising.advertising-id"] = new(["privacy"], "Disables unique advertising identifiers used for targeted cross-application tracking."),
        ["privacy.experiences.tailored-experiences"] = new(["privacy"], "Prevents Microsoft from offering tailored recommendations based on user diagnostic data."),
        ["privacy.activity.activity-history"] = new(["privacy"], "Disables publishing and cloud uploading of user application activity history."),
        ["privacy.speech.online-speech-recognition"] = new(["privacy"], "Disables cloud-based speech recognition and audio sample collection."),
        ["privacy.typing.inking-typing-personalization"] = new(["privacy"], "Disables harvesting of handwritten ink samples and typed keystroke dictionary data."),
        ["privacy.tracking.app-launch-tracking"] = new(["privacy", "ui"], "Disables tracking of application launch frequency used for Start menu personalization."),
        ["privacy.feedback.feedback-prompts"] = new(["privacy", "ui"], "Sets Windows feedback frequency to 'Never', suppressing survey prompts."),
        ["privacy.reporting.windows-error-reporting"] = new(["privacy"], "Disables automated Windows Error Reporting crash dump generation and upload."),
        ["privacy.ceip.customer-experience-improvement"] = new(["privacy"], "Disables participation in the Customer Experience Improvement Program (CEIP)."),
        ["privacy.clipboard.cloud-clipboard-sync"] = new(["privacy"], "Prevents clipboard copy history from synchronizing across multiple Windows devices."),
        ["privacy.search.device-search-history"] = new(["privacy", "ui"], "Disables recording of local search query history on the device."),
        ["privacy.spotlight.lockscreen-ads"] = new(["privacy", "ui"], "Disables promotional suggestions, tips, and Spotlight advertisements on the lock screen."),
        ["privacy.suggestions.start-menu-promotions"] = new(["privacy", "ui", "apps"], "Disables suggested app promotions and sponsored content in the Start Menu."),
        ["privacy.location.disable-sensor"] = new(["privacy"], "Disables the system-wide location sensor platform and hardware sensors."),
        ["privacy.consent.deny-location"] = new(["privacy", "apps"], "Configures default application permission to deny access to system location."),
        ["privacy.consent.deny-microphone"] = new(["privacy", "apps"], "Configures default application permission to deny access to microphone audio."),
        ["privacy.consent.deny-webcam"] = new(["privacy", "apps"], "Configures default application permission to deny access to cameras and webcams."),
        ["privacy.consent.deny-notifications"] = new(["privacy", "apps", "ui"], "Configures default application permission to deny access to user notifications."),
        ["privacy.consent.deny-account-info"] = new(["privacy", "apps"], "Configures default application permission to deny access to account profile details."),
        ["privacy.consent.deny-contacts"] = new(["privacy", "apps"], "Configures default application permission to deny access to address book contacts."),
        ["privacy.consent.deny-calendar"] = new(["privacy", "apps"], "Configures default application permission to deny access to user calendar appointments."),
        ["privacy.consent.deny-diagnostics"] = new(["privacy", "apps"], "Configures default application permission to deny access to diagnostic information from other apps."),
        ["system.security.disable-remote-assistance"] = new(["network", "privacy"], "Disables Windows Remote Assistance to block incoming remote help requests."),
        ["system.security.disable-autorun"] = new(["ui"], "Disables AutoRun and AutoPlay execution across all removable and fixed drives."),
        ["system.security.disable-wsh"] = new(["apps"], "Disables Windows Script Host (WSH) engine execution for VBScript and JScript files."),
        ["system.security.enforce-smartscreen"] = new(["apps", "network"], "Enforces Windows Defender SmartScreen file and website reputation checks."),
        ["system.security.disable-llmnr"] = new(["network"], "Disables Link-Local Multicast Name Resolution (LLMNR) to prevent local broadcast poisoning."),
        ["system.security.disable-rdp-auto-enable"] = new(["network"], "Enforces denial of inbound Remote Desktop connections to prevent unauthorized remote access."),
        ["system.security.disable-lockscreen-camera"] = new(["privacy", "ui"], "Disables camera capture access directly from the Windows lock screen without unlocking."),
        ["ui.system.enable-long-paths"] = new(["ui", "apps"], "Removes 260-character path length limitations for modern Win32 and .NET applications."),
        ["ui.explorer.classic-context-menu"] = new(["ui"], "Restores classic Windows 10 full right-click context menu in Windows 11 File Explorer."),
        ["ui.explorer.show-file-extensions"] = new(["ui"], "Always displays file extensions for known file types in File Explorer."),
        ["ui.explorer.show-hidden-files"] = new(["ui"], "Configures File Explorer to make hidden files and folders visible."),
        ["ui.explorer.open-to-this-pc"] = new(["ui"], "Sets File Explorer to open to 'This PC' by default instead of Home/Quick Access."),
        ["ui.taskbar.end-task-right-click"] = new(["ui", "performance"], "Adds an immediate 'End Task' action to taskbar application right-click menus."),
        ["ui.taskbar.show-seconds-clock"] = new(["ui"], "Displays seconds on the taskbar system tray clock."),
        ["ui.taskbar.alignment-left"] = new(["ui"], "Aligns Windows 11 taskbar icons to the left side like traditional Windows layouts."),
        ["ui.explorer.hide-gallery"] = new(["ui"], "Hides the Gallery folder entry from the File Explorer navigation pane."),
        ["ui.explorer.hide-recent-files"] = new(["ui", "privacy"], "Hides recently opened files from Quick Access and Home in File Explorer."),
        ["ui.explorer.hide-frequent-folders"] = new(["ui", "privacy"], "Hides frequently used folders from Quick Access and Home in File Explorer."),
        ["ui.explorer.show-full-path-title"] = new(["ui"], "Displays the complete folder file system path in the File Explorer title bar."),
        ["ui.search.disable-search-highlights"] = new(["ui", "privacy"], "Disables decorative search highlights and doodles inside the taskbar search box."),
        ["ui.taskbar.disable-widgets-button"] = new(["ui", "performance"], "Hides the Widgets weather icon button from the Windows 11 taskbar."),
        ["ui.taskbar.disable-chat-button"] = new(["ui"], "Hides the Microsoft Teams Chat button from the Windows 11 taskbar."),
        ["ui.taskbar.disable-task-view-button"] = new(["ui"], "Hides the Task View desktop switcher button from the Windows 11 taskbar."),
        ["ui.system.disable-welcome-experience"] = new(["ui"], "Suppresses the full-screen Windows Welcome Experience displayed after feature updates."),
        ["ui.system.disable-tips-suggestions"] = new(["ui"], "Disables tips, tricks, and operating system feature suggestions in Windows notifications."),
        ["ui.logon.hide-fast-user-switching"] = new(["ui"], "Hides the Fast User Switching button from the sign-in screen and Start menu."),
        ["updates.drivers.exclude-from-updates"] = new(["updates"], "Prevents Windows Update from automatically overwriting dedicated hardware drivers."),
        ["updates.reboot.disable-auto-reboot-logged-on"] = new(["updates"], "Prevents Windows Update from automatically rebooting the computer while a user is signed in."),
        ["updates.au.notify-download"] = new(["updates"], "Configures Automatic Updates to notify before downloading and installing updates."),
        ["updates.peering.metered-connections"] = new(["updates", "network"], "Blocks Delivery Optimization peer sharing when connected to metered or cellular networks."),
        ["updates.activehours.enable-policy"] = new(["updates"], "Enforces configured Active Hours range during which automatic restart will never occur."),
        ["updates.preview.disable-optional-content"] = new(["updates"], "Disables automatic installation of preview and optional non-security update rollouts.")
    };

    public static int Main(string[] args)
    {
        string rootDir = Directory.GetCurrentDirectory();
        while (rootDir != null && !File.Exists(Path.Combine(rootDir, "Tweakora.sln")))
        {
            rootDir = Directory.GetParent(rootDir)?.FullName!;
        }

        if (rootDir == null)
        {
            Console.WriteLine("[ERROR] Could not find repository root containing Tweakora.sln");
            return 1;
        }

        string tweaksDir = Path.Combine(rootDir, "Catalog", "Tweaks");
        string docsDir = Path.Combine(rootDir, "docs");
        string auditFile = Path.Combine(docsDir, "TAGS_AUDIT.md");

        var jsonFiles = Directory.GetFiles(tweaksDir, "*.json");
        int updatedCount = 0;

        var auditRows = new List<(string Id, string Name, string Tags, string Rationale)>();

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        foreach (var file in jsonFiles)
        {
            string content = File.ReadAllText(file);
            var rootNode = JsonNode.Parse(content);
            if (rootNode is not JsonArray array) continue;

            for (int i = 0; i < array.Count; i++)
            {
                var tweak = array[i];
                if (tweak is not JsonObject obj) continue;

                string id = obj["id"]?.ToString() ?? "";
                string name = obj["name"]?.ToString() ?? "";

                if (!TagMap.TryGetValue(id, out var info))
                {
                    Console.WriteLine($"[ERROR] Missing tag mapping for tweak ID '{id}' in {Path.GetFileName(file)}");
                    return 1;
                }

                // Create JsonArray for tags
                var tagsArr = new JsonArray();
                foreach (var tag in info.Tags)
                {
                    tagsArr.Add(tag);
                }

                obj["tags"] = tagsArr;
                updatedCount++;

                auditRows.Add((id, name, string.Join(", ", info.Tags), info.Rationale));
            }

            File.WriteAllText(file, rootNode.ToJsonString(options) + "\n");
            Console.WriteLine($"[UPDATED] {Path.GetFileName(file)}");
        }

        Console.WriteLine($"[SUCCESS] Updated {updatedCount} tweaks across {jsonFiles.Length} files.");

        // Generate docs/TAGS_AUDIT.md
        var sb = new StringBuilder();
        sb.AppendLine("# Tweakora Topic Tags Audit");
        sb.AppendLine();
        sb.AppendLine("This document provides a complete audit of semantic topic tags assigned to all catalog tweaks in WinVelo (Tweakora).");
        sb.AppendLine("Tags are assigned based on what each tweak actually does (its apply actions and functional effects), not from name alone.");
        sb.AppendLine();
        sb.AppendLine("Allowed topic tags: `gaming`, `privacy`, `ai`, `ui`, `performance`, `network`, `updates`, `apps`.");
        sb.AppendLine();
        sb.AppendLine($"Total catalog tweaks audited: **{auditRows.Count}**.");
        sb.AppendLine();
        sb.AppendLine("| Tweak ID | Name | Tags Assigned | Rationale (Actions & whatItDoes) |");
        sb.AppendLine("| :--- | :--- | :--- | :--- |");

        foreach (var row in auditRows.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"| `{row.Id}` | {row.Name} | `{row.Tags}` | {row.Rationale} |");
        }

        Directory.CreateDirectory(docsDir);
        File.WriteAllText(auditFile, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"[SUCCESS] Generated {auditFile}");

        return 0;
    }
}

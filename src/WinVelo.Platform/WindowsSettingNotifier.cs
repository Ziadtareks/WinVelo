using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WinVelo.Platform;

public static class WindowsSettingNotifier
{
    private const int HWND_BROADCAST = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint SMTO_NOTIMEOUTIFNOTHUNG = 0x0008;
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000; // Fast non-blocking shell notification (never SHCNF_FLUSH)

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        string? lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint wEventId,
        uint uFlags,
        IntPtr dwItem1,
        IntPtr dwItem2);

    /// <summary>
    /// Asynchronously broadcasts system setting changes and updates the Windows Explorer shell.
    /// Runs strictly on a background task with a strict 50ms timeout and non-blocking shell notifications.
    /// </summary>
    public static Task NotifySettingsChangedAsync(string? registryKey = null)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

                string? area = null;
                if (!string.IsNullOrEmpty(registryKey))
                {
                    if (registryKey.Contains("Personalize", StringComparison.OrdinalIgnoreCase) ||
                        registryKey.Contains("Themes", StringComparison.OrdinalIgnoreCase))
                    {
                        area = "ImmersiveColorSet";
                    }
                    else if (registryKey.Contains("Policy", StringComparison.OrdinalIgnoreCase) ||
                             registryKey.Contains("Policies", StringComparison.OrdinalIgnoreCase))
                    {
                        area = "Policy";
                    }
                    else if (registryKey.Contains("Environment", StringComparison.OrdinalIgnoreCase))
                    {
                        area = "Environment";
                    }
                }

                // Broadcast setting change to all top-level windows with a 50ms timeout to avoid hanging
                const uint fuFlags = SMTO_ABORTIFHUNG | SMTO_NOTIMEOUTIFNOTHUNG;
                SendMessageTimeout(
                    new IntPtr(HWND_BROADCAST),
                    WM_SETTINGCHANGE,
                    UIntPtr.Zero,
                    area,
                    fuFlags,
                    50,
                    out _);

                // Notify Explorer of shell / association / display updates using SHCNF_IDLIST (never blocks)
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // Non-fatal: if notification broadcast fails, do not throw
            }
        });
    }

    /// <summary>
    /// Broadcasts WM_SETTINGCHANGE once without an area and once for every area that the
    /// changed registry keys belong to (theme colors, policies, taskbar), so open windows,
    /// the taskbar and apps re-read the new values instead of waiting for a sign-out.
    /// </summary>
    public static async Task NotifySettingsChangedForKeysAsync(IEnumerable<string> registryKeys)
    {
        var keys = registryKeys?.Where(k => !string.IsNullOrWhiteSpace(k)).ToList() ?? new List<string>();
        await NotifySettingsChangedAsync().ConfigureAwait(false);
        if (keys.Any(k => k.Contains("Personalize", StringComparison.OrdinalIgnoreCase) || k.Contains("Themes", StringComparison.OrdinalIgnoreCase)))
            await NotifySettingsChangedAsync("Personalize").ConfigureAwait(false);
        if (keys.Any(k => k.Contains("Policies", StringComparison.OrdinalIgnoreCase)))
            await NotifySettingsChangedAsync("Policies").ConfigureAwait(false);
        if (keys.Any(k => k.Contains(@"Explorer\Advanced", StringComparison.OrdinalIgnoreCase) || k.Contains("Search", StringComparison.OrdinalIgnoreCase)))
            await BroadcastAreaAsync("TraySettings").ConfigureAwait(false);
    }

    private static Task BroadcastAreaAsync(string area)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
                SendMessageTimeout(new IntPtr(HWND_BROADCAST), WM_SETTINGCHANGE, UIntPtr.Zero, area,
                    SMTO_ABORTIFHUNG | SMTO_NOTIMEOUTIFNOTHUNG, 50, out _);
            }
            catch
            {
                // Non-fatal
            }
        });
    }

    /// <summary>
    /// Fire-and-forget non-blocking setting change notification.
    /// </summary>
    public static void NotifySettingChanged(string? registryKey = null)
    {
        _ = NotifySettingsChangedAsync(registryKey);
    }
}

using System;
using System.Runtime.InteropServices;

namespace Tweakora.Platform;

public static class WindowsSettingNotifier
{
    private const int HWND_BROADCAST = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_FLUSH = 0x1000;

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

    public static void NotifySettingChanged(string? registryKey = null)
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

            // Broadcast setting change to all top-level windows with a 150ms timeout to avoid hanging
            SendMessageTimeout(
                new IntPtr(HWND_BROADCAST),
                WM_SETTINGCHANGE,
                UIntPtr.Zero,
                area,
                SMTO_ABORTIFHUNG,
                150,
                out _);

            // Notify Explorer of shell / association / display updates
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Non-fatal: if notification broadcast fails, do not throw
        }
    }
}

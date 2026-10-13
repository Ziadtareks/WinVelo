using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Tweakora.Core.Safety;

[SupportedOSPlatform("windows")]
public static class AdminPrivilegeHelper
{
    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static void RestartAsAdministrator()
    {
        string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            Process.Start(psi);
            Environment.Exit(0);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled UAC prompt
        }
    }
}

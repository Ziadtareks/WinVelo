using System;
using Microsoft.Win32;

namespace WinVelo.Platform;

/// <summary>Reads the running Windows build number and EditionID (read-only).</summary>
public static class WindowsVersionInfo
{
    public static (int Build, string? EditionId) Read()
    {
        int build = Environment.OSVersion.Version.Build;
        string? edition = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                if (int.TryParse(key.GetValue("CurrentBuildNumber")?.ToString(), out int regBuild) && regBuild > 0)
                {
                    build = regBuild;
                }
                edition = key.GetValue("EditionID")?.ToString();
            }
        }
        catch
        {
            // Keep the Environment build; without an edition the edition check is skipped.
        }
        return (build, edition);
    }
}

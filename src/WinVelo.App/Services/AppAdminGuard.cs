using System.Windows;
using WinVelo.App.Localization;
using WinVelo.Core.Safety;

namespace WinVelo.App.Services;

public static class AppAdminGuard
{
    /// <summary>Marks a process started by <see cref="TryRelaunchElevated"/>, so a refused elevation never loops.</summary>
    public const string ElevatedRelaunchFlag = "--elevated-relaunch";

    /// <summary>
    /// In normal mode, starts WinVelo again with administrator rights (one UAC prompt) and returns true when that
    /// worked, so this process can exit. Returns false in test mode, when already elevated, after a relaunch, or
    /// when the user cancels the prompt; WinVelo then opens without administrator rights (changes are blocked
    /// by <see cref="EnsureAdmin"/> and the banner offers "Restart as Administrator").
    /// </summary>
    public static bool TryRelaunchElevated(string[] args)
    {
        if (TestModeManager.IsTestMode || AdminPrivilegeHelper.IsRunningAsAdministrator()) return false;
        if (args.Any(a => string.Equals(a, ElevatedRelaunchFlag, StringComparison.OrdinalIgnoreCase))) return false;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? "WinVelo.exe",
                UseShellExecute = true,
                Verb = "runas"
            };
            foreach (string a in args.Skip(1)) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add(ElevatedRelaunchFlag);
            return System.Diagnostics.Process.Start(psi) != null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // the user clicked "No" on the UAC prompt
        }
    }

    public static bool EnsureAdmin(string actionDescription)
    {
        if (TestModeManager.IsTestMode || AdminPrivilegeHelper.IsRunningAsAdministrator())
        {
            return true;
        }

        Serilog.Log.Information("Blocked without administrator rights: {Action}", actionDescription);
        MessageBox.Show(
            LocalizationManager.T("Administrator rights are needed for this change.\n\nClick 'Restart as Administrator' in the banner at the top, or start WinVelo again and accept the Windows prompt."),
            LocalizationManager.T("Administrator rights required"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return false;
    }
}

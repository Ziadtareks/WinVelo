using System.Windows;
using Tweakora.Core.Safety;

namespace Tweakora.App.Services;

public static class AppAdminGuard
{
    public static bool EnsureAdmin(string actionDescription)
    {
        if (TestModeManager.IsTestMode || AdminPrivilegeHelper.IsRunningAsAdministrator())
        {
            return true;
        }

        MessageBox.Show(
            $"Administrator privileges are required to {actionDescription}.\n\n" +
            "Please click 'Restart as Administrator' in the top banner or relaunch Tweakora as Administrator.",
            "Administrator Privileges Required",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return false;
    }
}

using WinVelo.Core.Models;

namespace WinVelo.Core.Interfaces;

public interface IAppSettingsService
{
    UserSettings GetSettings();
    void SaveSettings(UserSettings settings);
}

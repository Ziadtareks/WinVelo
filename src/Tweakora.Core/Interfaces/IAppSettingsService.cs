using Tweakora.Core.Models;

namespace Tweakora.Core.Interfaces;

public interface IAppSettingsService
{
    UserSettings GetSettings();
    void SaveSettings(UserSettings settings);
}

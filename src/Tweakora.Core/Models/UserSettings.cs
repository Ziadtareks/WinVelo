namespace Tweakora.Core.Models;

public sealed class UserSettings
{
    public bool HasCompletedFirstRunWizard { get; set; } = false;
    public string AppMode { get; set; } = "Simple"; // "Simple" or "Advanced"
    public bool RecommendedOnly { get; set; } = true;
    public bool ShowExperimental { get; set; } = false;
    public string Language { get; set; } = "en"; // "en" or "ar"
}

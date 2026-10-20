namespace WinVelo.Tests;

/// <summary>
/// The expected size of the built-in catalog. Tests compare against this so a tweak that is lost by accident
/// is noticed. When you add or remove a tweak on purpose, change the number here (and run tools/CatalogStats --update).
/// </summary>
internal static class CatalogCounts
{
    public const int Tweaks = 142;
    public const int Presets = 6;
}

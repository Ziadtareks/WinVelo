using System.Reflection;

namespace WinVelo.Core;

/// <summary>
/// Single source of truth for the product version shown in the app, the CLI, journals and diagnostics.
/// The value comes from &lt;Version&gt; in Directory.Build.props, so a release only changes that one line.
/// </summary>
public static class AppInfo
{
    public static string Version { get; } = ResolveVersion();

    /// <summary>Version with a leading "v", e.g. "v1.0.0".</summary>
    public static string DisplayVersion => "v" + Version;

    private static string ResolveVersion()
    {
        try
        {
            var assembly = typeof(AppInfo).Assembly;
            string? info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                return info.Split('+')[0]; // drop the "+<commit hash>" suffix added by the SDK
            }
            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }
}

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace WinVelo.Tests;

/// <summary>
/// A {StaticResource} that does not exist compiles fine but throws when the view opens (see BUG-001).
/// Every key a view uses must be defined in that view, App.xaml or a Styles/ dictionary.
/// </summary>
public sealed class XamlResourceTests
{
    private static string AppDir()
    {
        string? dir = System.AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "WinVelo.sln"))) dir = Path.GetDirectoryName(dir);
        Assert.False(string.IsNullOrEmpty(dir), "Repository root not found.");
        return Path.Combine(dir!, "src", "WinVelo.App");
    }

    private static HashSet<string> Keys(string xaml) =>
        Regex.Matches(xaml, "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void EveryStaticResourceUsedByAView_IsDefined()
    {
        string app = AppDir();
        var shared = Keys(File.ReadAllText(Path.Combine(app, "App.xaml")));
        foreach (string style in Directory.GetFiles(Path.Combine(app, "Styles"), "*.xaml"))
        {
            shared.UnionWith(Keys(File.ReadAllText(style)));
        }

        var missing = new List<string>();
        foreach (string view in Directory.GetFiles(Path.Combine(app, "Views"), "*.xaml"))
        {
            string xaml = File.ReadAllText(view);
            var local = Keys(xaml);
            foreach (Match m in Regex.Matches(xaml, @"\{StaticResource\s+([A-Za-z0-9_.]+)\}"))
            {
                string key = m.Groups[1].Value;
                if (!local.Contains(key) && !shared.Contains(key)) missing.Add($"{Path.GetFileName(view)}: {key}");
            }
        }

        Assert.True(missing.Count == 0, "Undefined StaticResource keys: " + string.Join(", ", missing.Distinct()));
    }
}

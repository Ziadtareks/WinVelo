using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace WinVelo.Tests;

/// <summary>
/// Windows refuses to start an exe whose embedded manifest is not valid XML ("side-by-side configuration is
/// incorrect"); the build does not catch it. A comment with "--" in it once broke WinVelo.exe this way.
/// </summary>
public sealed class AppManifestTests
{
    private static readonly XNamespace Asm1 = "urn:schemas-microsoft-com:asm.v1";
    private static readonly XNamespace Asm3 = "urn:schemas-microsoft-com:asm.v3";

    private static string ManifestPath()
    {
        string? dir = System.AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "WinVelo.sln"))) dir = Path.GetDirectoryName(dir);
        Assert.False(string.IsNullOrEmpty(dir), "Repository root not found.");
        return Path.Combine(dir!, "src", "WinVelo.App", "app.manifest");
    }

    [Fact]
    public void AppManifest_IsWellFormed_AndRunsAsInvoker()
    {
        // XDocument.Load throws on any XML error, including "--" inside a comment.
        XDocument doc = XDocument.Load(ManifestPath());

        Assert.Equal(Asm1 + "assembly", doc.Root!.Name);
        Assert.Single(doc.Root.Elements(Asm1 + "assemblyIdentity"));

        var level = Assert.Single(doc.Descendants(Asm3 + "requestedExecutionLevel"));
        Assert.Equal("asInvoker", (string?)level.Attribute("level"));
        Assert.Equal("false", (string?)level.Attribute("uiAccess"));
    }
}

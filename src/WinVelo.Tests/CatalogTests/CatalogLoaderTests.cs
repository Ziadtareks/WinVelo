using System;
using System.IO;
using System.Linq;
using WinVelo.Core.Catalog;
using Xunit;

namespace WinVelo.Tests.CatalogTests;

/// <summary>The catalog must load even when the Catalog folder is missing, empty or partly broken.</summary>
public sealed class CatalogLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WinVeloCatalogTests_" + Guid.NewGuid().ToString("N"));

    public CatalogLoaderTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private const string ValidTweak = """
        { "id": "test.valid.one", "name": "Valid", "description": "d", "category": "ui", "risk": "Safe",
          "detect": { "type": "Registry", "hive": "HKCU", "key": "Software\\WinVeloTest", "valueName": "A", "matchType": "Equals", "expectedValue": 1 },
          "apply": [ { "type": "Registry", "hive": "HKCU", "key": "Software\\WinVeloTest", "operation": "Set", "valueName": "A", "valueKind": "DWord", "data": 1 } ],
          "undo":  [ { "type": "Registry", "hive": "HKCU", "key": "Software\\WinVeloTest", "operation": "Delete", "valueName": "A" } ] }
        """;

    [Fact]
    public void MissingFolder_UsesBuiltInCatalog()
    {
        var source = CatalogSource.Create(Path.Combine(_root, "does-not-exist"));
        var result = CatalogLoader.LoadTweaks(source);

        Assert.False(source.UsesExternalFolder);
        Assert.True(result.Items.Count > 100, "The built-in catalog should hold all tweaks.");
        Assert.Empty(result.Issues);
        Assert.NotEmpty(CatalogLoader.LoadPresets(source).Items);
        Assert.NotNull(source.ReadText("Apps/applications.json"));
        Assert.NotNull(source.ReadText("Config/feature.json"));
        Assert.NotNull(source.ReadText("Localization/ar.json"));
    }

    [Fact]
    public void EmptyFolder_FallsBackToBuiltInFiles_AndReportsIt()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Catalog", "Tweaks"));
        var source = CatalogSource.Create(_root);
        var result = CatalogLoader.LoadTweaks(source);

        Assert.True(source.UsesExternalFolder);
        Assert.True(result.Items.Count > 100);
        Assert.Contains(result.Issues, i => i.File == "Catalog/Tweaks");
    }

    [Fact]
    public void BrokenJsonFile_IsSkipped_OtherFilesStillLoad()
    {
        string tweaks = Path.Combine(_root, "Catalog", "Tweaks");
        Directory.CreateDirectory(tweaks);
        File.WriteAllText(Path.Combine(tweaks, "broken.json"), "[ { \"id\": \"oops\", ");
        File.WriteAllText(Path.Combine(tweaks, "good.json"), "[" + ValidTweak + "]");

        var result = CatalogLoader.LoadTweaks(CatalogSource.FromFolderOnly(Path.Combine(_root, "Catalog")));

        Assert.Single(result.Items);
        Assert.Equal("test.valid.one", result.Items[0].Id);
        Assert.Contains(result.Issues, i => i.File == "Tweaks/broken.json");
    }

    [Fact]
    public void OneBrokenTweak_IsSkipped_RestOfTheFileLoads()
    {
        string tweaks = Path.Combine(_root, "Catalog", "Tweaks");
        Directory.CreateDirectory(tweaks);
        // "risk" must be a string; a number cannot be read into TweakDefinition.
        string broken = """{ "id": "test.broken", "name": "Broken", "risk": 5 }""";
        string noId = """{ "name": "No id" }""";
        File.WriteAllText(Path.Combine(tweaks, "mixed.json"), "[" + broken + "," + ValidTweak + "," + noId + "]");

        var result = CatalogLoader.LoadTweaks(CatalogSource.FromFolderOnly(Path.Combine(_root, "Catalog")));

        Assert.Equal(new[] { "test.valid.one" }, result.Items.Select(t => t.Id).ToArray());
        Assert.Contains(result.Issues, i => i.ItemId == "test.broken");
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void DuplicateId_SecondCopyIsSkipped()
    {
        string tweaks = Path.Combine(_root, "Catalog", "Tweaks");
        Directory.CreateDirectory(tweaks);
        File.WriteAllText(Path.Combine(tweaks, "a.json"), "[" + ValidTweak + "]");
        File.WriteAllText(Path.Combine(tweaks, "b.json"), "[" + ValidTweak + "]");

        var result = CatalogLoader.LoadTweaks(CatalogSource.FromFolderOnly(Path.Combine(_root, "Catalog")));

        Assert.Single(result.Items);
        Assert.Contains(result.Issues, i => i.File == "Tweaks/b.json" && i.ItemId == "test.valid.one");
    }

    [Fact]
    public void ExternalFile_ReplacesBuiltInFileWithSameName()
    {
        string tweaks = Path.Combine(_root, "Catalog", "Tweaks");
        Directory.CreateDirectory(tweaks);
        File.WriteAllText(Path.Combine(tweaks, "performance.json"), "[" + ValidTweak + "]");

        var builtIn = CatalogLoader.LoadTweaks(CatalogSource.EmbeddedOnly()).Items;
        var merged = CatalogLoader.LoadTweaks(CatalogSource.Create(_root)).Items;

        Assert.Contains(merged, t => t.Id == "test.valid.one");
        Assert.True(merged.Count < builtIn.Count + 1, "The built-in performance.json must be replaced, not added to.");
    }

    [Fact]
    public void BrokenSingleFile_ReturnsNullDocument_WithIssue()
    {
        string apps = Path.Combine(_root, "Catalog", "Apps");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "applications.json"), "{ not json");
        var issues = new System.Collections.Generic.List<CatalogIssue>();

        using var doc = CatalogLoader.LoadDocument(CatalogSource.FromFolderOnly(Path.Combine(_root, "Catalog")), "Apps/applications.json", issues);

        Assert.Null(doc);
        Assert.Single(issues);
    }

    [Theory]
    [InlineData("../secret.json")]
    [InlineData("Tweaks/../../secret.json")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("/etc/passwd")]
    public void RelativePathsCannotLeaveTheCatalogFolder(string path)
    {
        File.WriteAllText(Path.Combine(_root, "secret.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_root, "Catalog"));
        var source = CatalogSource.FromFolderOnly(Path.Combine(_root, "Catalog"));

        Assert.Null(source.ReadText(path));
        Assert.False(source.Exists(path));
    }
}

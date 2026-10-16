using System.Collections.Generic;
using Tweakora.Core;
using Tweakora.Core.Models;
using Xunit;

namespace Tweakora.Tests;

public class WindowsCompatibilityTests
{
    [Theory]
    [InlineData("Core", "Home")]
    [InlineData("CoreSingleLanguage", "Home")]
    [InlineData("Professional", "Pro")]
    [InlineData("ProfessionalWorkstation", "Pro")]
    [InlineData("ProfessionalEducation", "Education")]
    [InlineData("Enterprise", "Enterprise")]
    [InlineData("EnterpriseS", "Enterprise")]
    [InlineData("IoTEnterpriseS", "IoTEnterprise")]
    [InlineData("Education", "Education")]
    public void NormalizeEdition_MapsRegistryEditionIds(string editionId, string expected)
    {
        Assert.Equal(expected, WindowsCompatibility.NormalizeEdition(editionId));
    }

    [Fact]
    public void Check_BuildAndEditionRules()
    {
        var win11Only = new WindowsRequirements { MinBuild = 22000 };
        Assert.Equal(CompatibilityIssueKind.BuildTooOld, WindowsCompatibility.Check(win11Only, 19045, "Pro")!.Kind);
        Assert.Null(WindowsCompatibility.Check(win11Only, 26100, "Pro"));

        var upTo23H2 = new WindowsRequirements { MinBuild = 22621, MaxBuild = 22631 };
        Assert.Null(WindowsCompatibility.Check(upTo23H2, 22631, "Home"));
        Assert.Equal(CompatibilityIssueKind.BuildTooNew, WindowsCompatibility.Check(upTo23H2, 26100, "Home")!.Kind);

        var enterpriseOnly = new WindowsRequirements { MinBuild = 19041, Editions = new List<string> { "Enterprise", "Education" } };
        Assert.Equal(CompatibilityIssueKind.Edition, WindowsCompatibility.Check(enterpriseOnly, 26100, "Home")!.Kind);
        Assert.Null(WindowsCompatibility.Check(enterpriseOnly, 26100, "Enterprise"));
        Assert.Null(WindowsCompatibility.Check(enterpriseOnly, 26100, "IoTEnterprise"));
        // Unknown edition: never block on edition alone
        Assert.Null(WindowsCompatibility.Check(enterpriseOnly, 26100, string.Empty));
    }

    [Fact]
    public void Check_IsInactiveUntilConfigured()
    {
        var tweak = new TweakDefinition { Id = "test.win11", Windows = new WindowsRequirements { MinBuild = 99999 } };
        Assert.False(WindowsCompatibility.IsConfigured);
        Assert.Null(WindowsCompatibility.Check(tweak));
    }
}

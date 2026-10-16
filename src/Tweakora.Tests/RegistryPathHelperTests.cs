using System.Linq;
using Tweakora.Core.Utilities;
using Xunit;

namespace Tweakora.Tests;

public sealed class RegistryPathHelperTests
{
    [Fact]
    public void Normalize_CollapsesConsecutiveBackslashes()
    {
        var (hive, key) = RegistryPathHelper.Normalize("HKLM", @"SOFTWARE\\Policies\\\\Microsoft\\Windows\\System");
        Assert.Equal("HKLM", hive);
        Assert.Equal(@"SOFTWARE\Policies\Microsoft\Windows\System", key);
    }

    [Fact]
    public void Normalize_TrimsLeadingAndTrailingBackslashes()
    {
        var (hive, key) = RegistryPathHelper.Normalize("HKEY_CURRENT_USER", @"\\Software\Microsoft\GameBar\\\");
        Assert.Equal("HKCU", hive);
        Assert.Equal(@"Software\Microsoft\GameBar", key);
    }

    [Fact]
    public void Normalize_HandlesHivePathWithSubkeys()
    {
        var (hive, key) = RegistryPathHelper.Normalize(@"HKCU\Software\\Microsoft", @"\\\Windows");
        Assert.Equal("HKCU", hive);
        Assert.Equal(@"Software\Microsoft\Windows", key);
    }

    [Theory]
    [InlineData(@"Software\\Microsoft\\GameBar", true)]
    [InlineData(@"\SOFTWARE\Policies\Microsoft", true)]
    [InlineData(@"SOFTWARE\Policies\Microsoft\", true)]
    [InlineData(@"SOFTWARE\\Policies\Microsoft", true)]
    [InlineData(@"SOFTWARE\Policies\Microsoft\Windows", false)]
    public void RegistryKeyPathSegments_RejectsEmptySegments(string key, bool hasEmptySegment)
    {
        bool hasConsecutiveOrBorderSlashes = key.StartsWith('\\') || key.EndsWith('\\') || key.Contains(@"\\");
        var segments = key.Split('\\');
        bool containsEmptySegment = segments.Any(string.IsNullOrWhiteSpace);

        bool detected = hasConsecutiveOrBorderSlashes || containsEmptySegment;
        Assert.Equal(hasEmptySegment, detected);
    }
}

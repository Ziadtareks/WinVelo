using Tweakora.Core.Safety;
using Tweakora.Platform;
using Xunit;

namespace Tweakora.Tests.SafetyTests;

public class PlatformSafetyTests
{
    [Fact]
    public void TestMode_RealPlatformClassesNeverConstructed_WhenTestModeActive()
    {
        bool prev = TestModeManager.IsTestMode;
        try
        {
            TestModeManager.EnableTestMode();
            Assert.True(TestModeManager.IsTestMode);

            Assert.Throws<InvalidOperationException>(() => new WindowsRegistryService());
            Assert.Throws<InvalidOperationException>(() => new WindowsServicesService());
            Assert.Throws<InvalidOperationException>(() => new WindowsTaskSchedulerService());
            Assert.Throws<InvalidOperationException>(() => new WindowsRestoreService());
            Assert.Throws<InvalidOperationException>(() => new WindowsShellService());
        }
        finally
        {
            TestModeManager.IsTestMode = prev;
        }
    }
}

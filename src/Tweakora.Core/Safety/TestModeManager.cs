namespace Tweakora.Core.Safety;

public static class TestModeManager
{
    private static bool _isExplicitlySet;
    private static bool _testModeValue;

    public static bool IsTestMode
    {
        get
        {
            if (_isExplicitlySet)
            {
                return _testModeValue;
            }

            string? env = Environment.GetEnvironmentVariable("TWEAKORA_TEST_MODE");
            if (string.Equals(env, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(env, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                string[] args = Environment.GetCommandLineArgs();
                if (args.Any(a => string.Equals(a, "--test-mode", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            catch
            {
                // In some restricted or unit test runner contexts GetCommandLineArgs may throw
            }

            return false;
        }
        set
        {
            _isExplicitlySet = true;
            _testModeValue = value;
        }
    }

    public static void EnableTestMode()
    {
        IsTestMode = true;
    }

    public static void DisableTestMode()
    {
        IsTestMode = false;
    }

    public static void Reset()
    {
        _isExplicitlySet = false;
        _testModeValue = false;
    }
}

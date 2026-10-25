using System;
using Xunit;

namespace WinVelo.UiTests;

/// <summary>Visible desktop automation is opt-in; ordinary test runs never take focus.</summary>
public sealed class DesktopUiFactAttribute : FactAttribute
{
    public DesktopUiFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WINVELO_ALLOW_DESKTOP_UI_TESTS") != "1")
            Skip = "Desktop UI automation is disabled. Run on an isolated Windows runner with WINVELO_ALLOW_DESKTOP_UI_TESTS=1.";
    }
}

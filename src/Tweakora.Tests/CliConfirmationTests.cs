using System;
using Tweakora.Cli;
using Xunit;

namespace Tweakora.Tests;

[Collection("CliConsoleTests")]
public sealed class CliConfirmationTests
{
    [Fact]
    public void CheckConfirmationGate_WithYesFlag_ReturnsTrue()
    {
        string[] args = new[] { "--preset", "gaming", "--run", "--yes" };
        bool confirmed = Program.CheckConfirmationGate(args, "Test operation");
        Assert.True(confirmed);
    }

    [Fact]
    public void CheckConfirmationGate_WithShortYFlag_ReturnsTrue()
    {
        string[] args = new[] { "--apply", "--tweaks", "essential.activity", "-y" };
        bool confirmed = Program.CheckConfirmationGate(args, "Test operation");
        Assert.True(confirmed);
    }

    [Fact]
    public void CheckConfirmationGate_WithTestMode_ReturnsTrue()
    {
        string[] args = new[] { "--test-mode", "--preset", "gaming", "--run" };
        bool confirmed = Program.CheckConfirmationGate(args, "Test operation");
        Assert.True(confirmed);
    }

    [Fact]
    public void CheckConfirmationGate_WithoutYesOrTestMode_InNonInteractiveEnv_ReturnsFalse()
    {
        // When running in test runner, Console.IsInputRedirected is true
        if (Console.IsInputRedirected)
        {
            string[] args = new[] { "--preset", "gaming", "--run" };
            bool confirmed = Program.CheckConfirmationGate(args, "Real system modification");
            Assert.False(confirmed);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task CliInfoCommand_ValidTweak_OutputsDetailsAndReturnsZero()
    {
        var originalOut = Console.Out;
        using var sw = new System.IO.StringWriter();
        try
        {
            Console.SetOut(sw);
            int exitCode = await Program.Main(new[] { "--info", "ui.explorer.show-file-extensions" });
            Assert.Equal(0, exitCode);

            string output = sw.ToString();
            Assert.Contains("TWEAK INFORMATION: ui.explorer.show-file-extensions", output);
            Assert.Contains("EXACT CHANGES:", output);
            Assert.Contains("HideFileExt = 0 (DWord)", output);
            Assert.Contains("WHAT IT DOES:", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}

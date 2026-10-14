using System;
using Tweakora.Cli;
using Xunit;

namespace Tweakora.Tests;

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
}

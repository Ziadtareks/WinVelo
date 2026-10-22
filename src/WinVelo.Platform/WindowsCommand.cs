using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;

namespace WinVelo.Platform;

internal static class WindowsCommand
{
    public static PowerShellResult Run(IShellService shell, string executable, int timeout, params string[] arguments)
        => shell.RunProcessAsync(executable, arguments, timeout).GetAwaiter().GetResult();

    public static string RequireSuccess(PowerShellResult result, string command)
    {
        if (!result.Success)
            throw new InvalidOperationException($"{command} failed (exit {result.ExitCode}): " +
                (result.TimedOut ? "Timed out." : !string.IsNullOrWhiteSpace(result.StdErr) ? result.StdErr.Trim() : result.StdOut.Trim()));
        return result.StdOut;
    }
}

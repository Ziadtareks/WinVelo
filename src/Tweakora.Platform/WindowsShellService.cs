using System.Diagnostics;
using System.Runtime.InteropServices;
using Tweakora.Core.Interfaces;
using Tweakora.Core.Safety;

namespace Tweakora.Platform;

public sealed class WindowsShellService : IShellService
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    private const int HWND_BROADCAST = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    public WindowsShellService()
    {
        EnsureNotTestMode();
    }

    private static void EnsureNotTestMode()
    {
        if (TestModeManager.IsTestMode)
        {
            throw new InvalidOperationException("Real platform class operation blocked in TEST MODE.");
        }
    }

    public void RestartExplorer()
    {
        EnsureNotTestMode();
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); } catch { }
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true
        });
    }

    public void RefreshShellEnvironment()
    {
        EnsureNotTestMode();
        try
        {
            SendMessageTimeout(
                (IntPtr)HWND_BROADCAST,
                WM_SETTINGCHANGE,
                UIntPtr.Zero,
                "Environment",
                SMTO_ABORTIFHUNG,
                2000,
                out _);
        }
        catch { }
    }

    public async Task<string> ExecutePowerShellAsync(string script, int timeoutSeconds = 60)
    {
        EnsureNotTestMode();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 300)));
        try
        {
            string output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            string error = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (!string.IsNullOrWhiteSpace(error) && process.ExitCode != 0)
            {
                throw new InvalidOperationException($"PowerShell script error (ExitCode {process.ExitCode}): {error}");
            }

            return output;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException($"PowerShell script timed out after {timeoutSeconds} seconds.");
        }
    }

    public void OpenUrl(string url)
    {
        // Opening a URL in the user's browser is safe and non-mutating
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    public void LaunchApplet(string appletCommand)
    {
        EnsureNotTestMode();
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start {appletCommand}",
            UseShellExecute = true,
            CreateNoWindow = true
        });
    }
}


using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;

namespace WinVelo.Platform;

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
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }

        // Windows normally relaunches the shell by itself. Only start it when it did not come back,
        // otherwise a second explorer.exe would just open an extra File Explorer window.
        for (int i = 0; i < 10; i++)
        {
            Thread.Sleep(500);
            if (Process.GetProcessesByName("explorer").Length > 0) return;
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

    public async Task<PowerShellResult> ExecutePowerShellAsync(string script, int timeoutSeconds = 60)
    {
        EnsureNotTestMode();

        int effectiveTimeout = timeoutSeconds <= 0 ? 60 : Math.Min(timeoutSeconds, 3600);

        string wrappedScript =
            "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';\r\n" +
            "try {\r\n" +
            script + "\r\n" +
            "} catch {\r\n" +
            "    [Console]::Error.WriteLine($_)\r\n" +
            "    exit 1\r\n" +
            "}\r\n";

        byte[] scriptBytes = Encoding.Unicode.GetBytes(wrappedScript);
        string encodedCommand = Convert.ToBase64String(scriptBytes);

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new PowerShellResult
            {
                ExitCode = -1,
                StdOut = string.Empty,
                StdErr = ex.Message,
                TimedOut = false,
                Duration = stopwatch.Elapsed
            };
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(effectiveTimeout));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cts.Token);
            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            stopwatch.Stop();

            return new PowerShellResult
            {
                ExitCode = process.ExitCode,
                StdOut = stdout,
                StdErr = stderr,
                TimedOut = false,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            string stdout = string.Empty;
            string stderr = string.Empty;
            try
            {
                if (stdoutTask.IsCompleted) stdout = await stdoutTask;
                if (stderrTask.IsCompleted) stderr = await stderrTask;
            }
            catch
            {
            }

            return new PowerShellResult
            {
                ExitCode = -1,
                StdOut = stdout,
                StdErr = stderr,
                TimedOut = true,
                Duration = stopwatch.Elapsed
            };
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


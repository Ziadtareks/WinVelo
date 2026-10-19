using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using WinVelo.Core.Interfaces;
using WinVelo.Core.Models;
using WinVelo.Core.Safety;
using WinVelo.Core.Utilities;

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

        return await RunCoreAsync(
            "powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encodedCommand },
            effectiveTimeout,
            CancellationToken.None,
            outputEncoding: null);
    }

    public Task<PowerShellResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
    {
        EnsureNotTestMode();
        int effectiveTimeout = timeoutSeconds <= 0 ? 60 : Math.Min(timeoutSeconds, 3600);
        return RunCoreAsync(fileName, arguments, effectiveTimeout, cancellationToken, Encoding.UTF8);
    }

    private static async Task<PowerShellResult> RunCoreAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken,
        Encoding? outputEncoding)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (outputEncoding != null)
        {
            startInfo.StandardOutputEncoding = outputEncoding;
            startInfo.StandardErrorEncoding = outputEncoding;
        }
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };

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
                FailedToStart = true,
                Duration = stopwatch.Elapsed
            };
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
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

            bool cancelledByCaller = cancellationToken.IsCancellationRequested;
            return new PowerShellResult
            {
                ExitCode = -1,
                StdOut = stdout,
                StdErr = cancelledByCaller ? "Cancelled." : stderr,
                TimedOut = !cancelledByCaller,
                Cancelled = cancelledByCaller,
                Duration = stopwatch.Elapsed
            };
        }
    }

    public void OpenUrl(string url)
    {
        // Only http(s) links: a catalog value must never start a program or open a local file.
        if (!SafeUrl.IsWebUrl(url)) return;
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


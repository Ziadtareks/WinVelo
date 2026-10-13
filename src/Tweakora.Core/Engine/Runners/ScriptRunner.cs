using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tweakora.Core.Engine.Runners;

public sealed class ScriptExecutionResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

public sealed class ScriptRunner
{
    public async Task<ScriptExecutionResult> RunPowerShellAsync(
        string script,
        Action<string>? onOutputLine = null,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                onOutputLine?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
                onOutputLine?.Invoke($"[STDERR] {e.Data}");
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 300)));

            await process.WaitForExitAsync(cts.Token);
            return new ScriptExecutionResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Output = outputBuilder.ToString(),
                Error = errorBuilder.ToString()
            };
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            return new ScriptExecutionResult
            {
                Success = false,
                ExitCode = -1,
                Output = outputBuilder.ToString(),
                Error = "Script execution timed out or was cancelled."
            };
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult
            {
                Success = false,
                ExitCode = -1,
                Output = outputBuilder.ToString(),
                Error = ex.Message
            };
        }
    }

    public async Task<ScriptExecutionResult> RunCmdAsync(
        string command,
        Action<string>? onOutputLine = null,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                onOutputLine?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
                onOutputLine?.Invoke($"[STDERR] {e.Data}");
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 300)));

            await process.WaitForExitAsync(cts.Token);
            return new ScriptExecutionResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Output = outputBuilder.ToString(),
                Error = errorBuilder.ToString()
            };
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            return new ScriptExecutionResult
            {
                Success = false,
                ExitCode = -1,
                Output = outputBuilder.ToString(),
                Error = "Command execution timed out or was cancelled."
            };
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult
            {
                Success = false,
                ExitCode = -1,
                Output = outputBuilder.ToString(),
                Error = ex.Message
            };
        }
    }
}

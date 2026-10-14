using System;

namespace Tweakora.Core.Models;

/// <summary>
/// Structured execution result from a PowerShell script invocation.
/// </summary>
public sealed class PowerShellResult
{
    public required int ExitCode { get; init; }
    public required string StdOut { get; init; }
    public required string StdErr { get; init; }
    public required bool TimedOut { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// True only if the process exited with code 0 and did not time out.
    /// </summary>
    public bool Success => !TimedOut && ExitCode == 0;
}

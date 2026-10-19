namespace WinVelo.Core.Updates;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, string? LatestVersion = null, string? ReleaseUrl = null);

/// <summary>
/// Optional, user-initiated update check. Implementations must not run on startup or in the background.
/// </summary>
public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default);
}

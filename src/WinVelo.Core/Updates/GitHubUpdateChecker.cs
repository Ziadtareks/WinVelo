using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace WinVelo.Core.Updates;

/// <summary>
/// Reads the latest public release from GitHub. This is only called when the user clicks
/// "Check for updates"; it is not a background poll and it never downloads the release.
/// </summary>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    internal const string LatestReleaseApiUrl = "https://api.github.com/repos/Ziadtareks/WinVelo/releases/latest";
    internal const string ReleasesPageUrl = "https://github.com/Ziadtareks/WinVelo/releases/latest";

    private readonly HttpClient _httpClient;

    public GitHubUpdateChecker(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
            request.Headers.UserAgent.ParseAdd($"WinVelo/{AppInfo.Version}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed);
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            JsonElement root = document.RootElement;

            if (IsTrue(root, "draft") || IsTrue(root, "prerelease"))
            {
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, AppInfo.DisplayVersion);
            }

            string? tag = root.TryGetProperty("tag_name", out JsonElement tagElement) ? tagElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed);
            }

            string latestVersion = tag.StartsWith('v') || tag.StartsWith('V') ? tag : "v" + tag;
            if (!IsNewerVersion(tag, currentVersion))
            {
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, latestVersion);
            }

            string? url = root.TryGetProperty("html_url", out JsonElement urlElement) ? urlElement.GetString() : null;
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, latestVersion, SafeReleaseUrl(url));
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
    }

    /// <summary>Compares "v1.0.10" with "1.0.9" numerically, ignoring a leading v and any suffix.</summary>
    public static bool IsNewerVersion(string latestTag, string currentVersion)
    {
        if (!TryParseVersion(latestTag, out Version? latest) || !TryParseVersion(currentVersion, out Version? current))
        {
            return false;
        }

        return latest > current;
    }

    private static bool TryParseVersion(string value, out Version? version)
    {
        string cleaned = value.Trim();
        if (cleaned.StartsWith('v') || cleaned.StartsWith('V'))
        {
            cleaned = cleaned[1..];
        }

        int suffix = cleaned.IndexOfAny(['-', '+']);
        if (suffix >= 0)
        {
            cleaned = cleaned[..suffix];
        }

        return Version.TryParse(cleaned, out version);
    }

    private static bool IsTrue(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement element) &&
        element.ValueKind == JsonValueKind.True;

    private static string SafeReleaseUrl(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith("/Ziadtareks/WinVelo/releases/", StringComparison.Ordinal))
        {
            return uri.AbsoluteUri;
        }

        return ReleasesPageUrl;
    }
}

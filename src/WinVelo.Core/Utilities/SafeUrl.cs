using System.Diagnostics.CodeAnalysis;

namespace WinVelo.Core.Utilities;

/// <summary>
/// Links from the catalog are opened with the shell. Only absolute http(s) URLs are allowed, so a catalog entry
/// can never make WinVelo (which runs as administrator) start a program, open a file or use another protocol.
/// </summary>
public static class SafeUrl
{
    public static bool IsWebUrl([NotNullWhen(true)] string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)) return false;
        return (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
               !string.IsNullOrEmpty(uri.Host) &&
               !uri.IsUnc;
    }
}

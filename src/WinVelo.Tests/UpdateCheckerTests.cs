using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinVelo.Core.Updates;
using Xunit;

namespace WinVelo.Tests;

public sealed class UpdateCheckerTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private static GitHubUpdateChecker CreateChecker(HttpStatusCode status, string body) =>
        new(new HttpClient(new StubHandler(status, body)));

    [Fact]
    public async Task NewerRelease_IsReportedAsAvailable()
    {
        var checker = CreateChecker(HttpStatusCode.OK, """
            {"tag_name":"v1.0.4","html_url":"https://github.com/Ziadtareks/WinVelo/releases/tag/v1.0.4","draft":false,"prerelease":false}
            """);

        var result = await checker.CheckAsync("1.0.3");

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("v1.0.4", result.LatestVersion);
        Assert.Equal("https://github.com/Ziadtareks/WinVelo/releases/tag/v1.0.4", result.ReleaseUrl);
    }

    [Fact]
    public async Task SameRelease_IsUpToDate()
    {
        var checker = CreateChecker(HttpStatusCode.OK, """
            {"tag_name":"v1.0.3","html_url":"https://github.com/Ziadtareks/WinVelo/releases/tag/v1.0.3","draft":false,"prerelease":false}
            """);

        var result = await checker.CheckAsync("1.0.3");

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.ReleaseUrl);
    }

    [Fact]
    public async Task Prerelease_IsNotOfferedAsAnUpdate()
    {
        var checker = CreateChecker(HttpStatusCode.OK, """
            {"tag_name":"v1.0.4-beta.1","html_url":"https://github.com/Ziadtareks/WinVelo/releases/tag/v1.0.4-beta.1","draft":false,"prerelease":true}
            """);

        var result = await checker.CheckAsync("1.0.3");

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.ReleaseUrl);
    }

    [Fact]
    public async Task ServerError_IsReportedAsFailed()
    {
        var checker = CreateChecker(HttpStatusCode.InternalServerError, "{}");

        var result = await checker.CheckAsync("1.0.3");

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task UntrustedReleaseUrl_FallsBackToTheKnownReleasesPage()
    {
        var checker = CreateChecker(HttpStatusCode.OK, """
            {"tag_name":"v1.0.4","html_url":"https://evil.example/winvelo","draft":false,"prerelease":false}
            """);

        var result = await checker.CheckAsync("1.0.3");

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("https://github.com/Ziadtareks/WinVelo/releases/latest", result.ReleaseUrl);
    }

    [Theory]
    [InlineData("v1.0.10", "1.0.9", true)]
    [InlineData("1.0.4", "1.0.3", true)]
    [InlineData("v1.0.3", "1.0.3", false)]
    [InlineData("v1.0.2", "1.0.3", false)]
    [InlineData("v1.0.4-beta.1", "1.0.3", true)]
    [InlineData("not-a-version", "1.0.3", false)]
    public void IsNewerVersion_ComparesVersionsSemantically(string tag, string current, bool expected)
    {
        Assert.Equal(expected, GitHubUpdateChecker.IsNewerVersion(tag, current));
    }
}

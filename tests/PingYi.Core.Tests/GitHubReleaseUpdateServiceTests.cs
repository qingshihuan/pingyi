using System.Net;
using System.Text;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class GitHubReleaseUpdateServiceTests
{
    [Theory]
    [InlineData("v0.3.1", 0, 3, 1)]
    [InlineData("1.2.0-beta.2+sha", 1, 2, 0)]
    public void TryParseVersion_StripsTagPrefixAndPrereleaseMetadata(
        string value,
        int major,
        int minor,
        int build)
    {
        Assert.True(GitHubReleaseUpdateService.TryParseVersion(value, out var parsed));
        Assert.Equal(new Version(major, minor, build), parsed);
    }

    [Fact]
    public async Task CheckAsync_UsesOfficialMetadataEndpointAndParsesStableVersion()
    {
        using var handler = new StubHandler(request =>
        {
            Assert.Equal(
                "https://api.github.com/repos/qingshihuan/pingyi/releases/latest",
                request.RequestUri?.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"tag_name\":\"v99.4.2\",\"html_url\":\"https://github.com/qingshihuan/pingyi/releases/tag/v99.4.2\"}",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var client = new HttpClient(handler);

        var result = await new GitHubReleaseUpdateService(client).CheckAsync();

        Assert.Equal(new Version(99, 4, 2), result.LatestVersion);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("github.com", result.ReleasePage.Host);
    }

    [Fact]
    public async Task CheckAsync_RejectsNonGitHubReleasePage()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"tag_name\":\"v99.4.2\",\"html_url\":\"https://example.test/download.exe\"}",
                Encoding.UTF8,
                "application/json")
        });
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new GitHubReleaseUpdateService(client).CheckAsync());
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}

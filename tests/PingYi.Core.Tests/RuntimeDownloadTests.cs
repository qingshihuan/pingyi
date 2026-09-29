using System.Net;
using System.Security.Cryptography;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class RuntimeDownloadTests
{
    private static readonly byte[] Payload = "synthetic executable archive for testing only"u8.ToArray();
    private static RuntimeAsset Asset => new("b11146", "llama-test.zip", Payload.Length, Convert.ToHexString(SHA256.HashData(Payload)));
    private static HttpResponseMessage Data(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    [Fact]
    public async Task Header_timeout_tries_opted_in_relay_and_checks_bytes()
    {
        var hosts = new List<string>();
        using var http = new HttpClient(new Handler(async (r, token) =>
        {
            hosts.Add(r.RequestUri!.Host);
            if (hosts.Count == 1) await Task.Delay(Timeout.Infinite, token);
            return Data(Payload);
        }));
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "package");
            await new RuntimeDownloader(http, TimeSpan.FromMilliseconds(30)).DownloadAsync(Asset, path, true, null, default, ["https://relay.example/"]);
            Assert.Equal(new[] { "github.com", "relay.example" }, hosts);
            Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
            Assert.False(File.Exists(path + ".partial"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task Hash_mismatch_does_not_install_or_trust_relay_metadata()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Data(++calls == 1 ? new byte[Payload.Length] : Payload))));
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "package");
            await new RuntimeDownloader(http).DownloadAsync(Asset, path, true, null, default, ["https://relay.example/"]);
            Assert.Equal(2, calls);
            Assert.True(await RuntimeDownloader.VerifyAsync(path, Asset, default));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task Resume_requires_matching_content_range_and_full_digest()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "package");
        await File.WriteAllBytesAsync(path + ".partial", Payload[..5]);
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(5, Assert.Single(request.Headers.Range!.Ranges).From);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Payload[5..]) };
            response.Content.Headers.ContentRange = new(5, Payload.Length - 1, Payload.Length);
            return Task.FromResult(response);
        }));
        try
        {
            await new RuntimeDownloader(http).DownloadAsync(Asset, path, false, null, default);
            Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task User_cancellation_does_not_try_another_source()
    {
        var calls = 0;
        using var cancel = new CancellationTokenSource();
        using var http = new HttpClient(new Handler((_, token) => { calls++; cancel.Cancel(); return Task.FromCanceled<HttpResponseMessage>(token); }));
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RuntimeDownloader(http).DownloadAsync(Asset, Path.Combine(dir, "package"), true, null, cancel.Token));
            Assert.Equal(1, calls);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task Insecure_redirect_is_never_requested()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("http://insecure.example/runtime.zip");
            return Task.FromResult(response);
        }));
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.ThrowsAsync<ProviderException>(() => new RuntimeDownloader(http).DownloadAsync(Asset, Path.Combine(dir, "package"), false, null, default));
            Assert.Equal(1, calls);
            Assert.False(File.Exists(Path.Combine(dir, "package")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public void Third_party_sources_require_explicit_opt_in()
    {
        Assert.Single(RuntimeDownloader.Sources(Asset, false));
        Assert.True(RuntimeDownloader.Sources(Asset, true).Count >= 3);
        Assert.Throws<ArgumentException>(() => RuntimeDownloader.Sources(Asset, true, ["http://relay.example/"]));
    }
    [Theory]
    [InlineData("cpu")] [InlineData("vulkan")] [InlineData("cuda12")] [InlineData("cuda13")] [InlineData("rocm")]
    public void Offline_catalogue_has_verified_platform_specific_files(string backend)
    {
        foreach (var windows in new[] { false, true })
        {
            var bundle = RuntimeCatalog.Pinned(backend, windows);
            Assert.Equal("b11146", bundle.Tag);
            Assert.Equal(backend.StartsWith("cuda") ? 2 : 1, bundle.Assets.Length);
            foreach (var asset in bundle.Assets) { asset.Validate(); Assert.Contains(windows ? "win" : "ubuntu", asset.Name); }
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}

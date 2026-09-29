using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class RuntimeCatalogTests
{
    private const string Tag = "b12345";
    private static JsonObject Asset(string name) => new()
    {
        ["id"] = 123, ["name"] = name, ["size"] = 1024,
        ["digest"] = "sha256:" + new string('a', 64), ["state"] = "uploaded",
        ["browser_download_url"] = $"https://github.com/ggml-org/llama.cpp/releases/download/{Tag}/{name}"
    };
    private static JsonObject Release(params JsonObject[] assets) => new()
    {
        ["tag_name"] = Tag, ["draft"] = false, ["assets"] = new JsonArray(assets)
    };
    private static RuntimeBundle Parse(JsonObject data, string backend = "cpu", bool windows = true)
    {
        using var document = JsonDocument.Parse(data.ToJsonString());
        return RuntimeCatalog.ParseOfficialRelease(document.RootElement, backend, windows);
    }
    [Fact]
    public void Missing_digest_and_redirected_download_identity_are_rejected()
    {
        var asset = Asset($"llama-{Tag}-bin-win-cpu-x64.zip");
        asset["digest"] = null;
        Assert.Throws<InvalidDataException>(() => Parse(Release(asset)));
        asset = Asset($"llama-{Tag}-bin-win-cpu-x64.zip");
        asset["browser_download_url"] = "https://untrusted.example/runtime.zip";
        Assert.Throws<InvalidDataException>(() => Parse(Release(asset)));
    }
    [Fact]
    public void Cuda_requires_same_platform_version_runtime_dependencies()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Release(Asset($"llama-{Tag}-bin-win-cuda-13.4-x64.zip")), "cuda13"));
        var bundle = Parse(Release(Asset($"llama-{Tag}-bin-win-cuda-13.4-x64.zip"), Asset("cudart-llama-bin-win-cuda-13.4-x64.zip")), "cuda13");
        Assert.Equal(2, bundle.Assets.Length);
        Assert.Equal(Tag, bundle.Tag);
    }
    [Fact]
    public void Ambiguous_platform_assets_are_not_silently_chosen()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Release(
            Asset($"llama-{Tag}-bin-win-cpu-x64.zip"), Asset($"llama-{Tag}-bin-win-cpu-x64.zip"))));
        Assert.Throws<InvalidDataException>(() => Parse(Release(Asset($"llama-{Tag}-bin-ubuntu-x64.tar.gz"))));
    }
    [Fact]
    public async Task Stable_release_resolves_its_unique_official_binary_build()
    {
        var calls = new List<string>();
        using var client = new HttpClient(new Handler(request =>
        {
            calls.Add(request.RequestUri!.AbsoluteUri);
            return calls.Count == 1 ? new JsonObject
            {
                ["tag_name"] = "v0.5.0",
                ["body"] = $"Nightly build: https://github.com/ggml-org/llama.cpp/releases/tag/{Tag}"
            } : Release(Asset($"llama-{Tag}-bin-win-cpu-x64.zip"));
        }));
        var result = await new RuntimeCatalog(client).ResolveAsync("cpu", true, default);
        Assert.Equal(Tag, result.Tag);
        Assert.False(result.IsPinnedFallback);
        Assert.Equal(new[] {
            "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest",
            "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/" + Tag }, calls);
    }
    [Fact]
    public async Task Unavailable_official_metadata_uses_explicitly_labelled_embedded_catalogue()
    {
        using var client = new HttpClient(new Handler(_ => throw new HttpRequestException("Synthetic offline condition")));
        var result = await new RuntimeCatalog(client).ResolveAsync("cuda13", true, default);
        Assert.True(result.IsPinnedFallback);
        Assert.Equal("b11146", result.Tag);
        Assert.Equal(2, result.Assets.Length);
    }
    private sealed class Handler(Func<HttpRequestMessage, JsonObject> value) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(value(request).ToJsonString()) });
    }
}

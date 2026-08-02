using System.Net;
using System.Text;
using System.Text.Json;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class ChatCompatibleTranslationProviderTests
{
    [Fact]
    [Trait("Category", "LocalLlama")]
    public async Task LocalLlama_AnswersRealTranslationRequestWhenEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PINGYI_RUN_LLAMA_TESTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var provider = new ChatCompatibleTranslationProvider(
            httpClient,
            new StubSecretStore(),
            () => new AppSettings());

        var availability = await provider.GetAvailabilityAsync();
        var result = await provider.TranslateAsync(new TranslationRequest("Hello", "en", "zh"));

        Assert.True(availability.IsAvailable, availability.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Text));
    }

    [Fact]
    public async Task LocalAvailability_ValidatesConfiguredModelAgainstModelsEndpoint()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("http://127.0.0.1:8080/v1/models", request.RequestUri?.AbsoluteUri);
            return Json("{\"data\":[{\"id\":\"gemma-4-e4b-it\"}]}");
        });
        var settings = new AppSettings();
        var provider = new ChatCompatibleTranslationProvider(
            new HttpClient(handler),
            new StubSecretStore(),
            () => settings);

        var availability = await provider.GetAvailabilityAsync();

        Assert.True(availability.IsAvailable);
    }

    [Fact]
    public async Task LocalAvailability_ExplainsModelNameMismatch()
    {
        var handler = new StubHandler(_ => Json("{\"data\":[{\"id\":\"gemma-4-e4b-it\"}]}"));
        var settings = new AppSettings { CustomTranslationModel = "gemma4" };
        var provider = new ChatCompatibleTranslationProvider(
            new HttpClient(handler),
            new StubSecretStore(),
            () => settings);

        var availability = await provider.GetAvailabilityAsync();

        Assert.False(availability.IsAvailable);
        Assert.Contains("gemma-4-e4b-it", availability.Message);
    }

    [Fact]
    public async Task Translate_RepairsLocalV1BaseUrlBeforePosting()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://127.0.0.1:8080/v1/chat/completions", request.RequestUri?.AbsoluteUri);
            return Json("{\"choices\":[{\"message\":{\"content\":\"你好\"}}]}");
        });
        var settings = new AppSettings { CustomTranslationEndpoint = "http://127.0.0.1:8080/v1" };
        var provider = new ChatCompatibleTranslationProvider(
            new HttpClient(handler),
            new StubSecretStore(),
            () => settings);

        var result = await provider.TranslateAsync(new TranslationRequest("Hello", "en", "zh"));

        Assert.Equal("你好", result.Text);
    }

    [Fact]
    public async Task OllamaPreset_DiscoversModelsThroughOpenAiCompatibilityEndpoint()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("http://127.0.0.1:11434/v1/models", request.RequestUri?.AbsoluteUri);
            return Json("{\"data\":[{\"id\":\"qwen3:8b\"},{\"id\":\"gemma3:4b\"}]}");
        });
        var settings = new AppSettings
        {
            CustomTranslationEndpoint = "http://127.0.0.1:11434",
            CustomTranslationModel = "qwen3:8b"
        };
        var provider = new ChatCompatibleTranslationProvider(
            new HttpClient(handler),
            new StubSecretStore(),
            () => settings);

        var models = await provider.GetAvailableModelsAsync();

        Assert.Equal(["qwen3:8b", "gemma3:4b"], models);
    }

    [Fact]
    public async Task Translate_AllowsModelDetectedSourceAndMultilingualTarget()
    {
        var handler = new StubHandler(request =>
        {
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(payload);
            var prompt = document.RootElement
                .GetProperty("messages")[0]
                .GetProperty("content")
                .GetString();
            Assert.Contains("自动识别输入语言", prompt);
            Assert.Contains("翻译为日语", prompt);
            return Json("{\"choices\":[{\"message\":{\"content\":\"こんにちは\"}}]}");
        });
        var provider = new ChatCompatibleTranslationProvider(
            new HttpClient(handler),
            new StubSecretStore(),
            () => new AppSettings());

        var result = await provider.TranslateAsync(
            new TranslationRequest("Good morning", LanguageCatalog.Auto, "ja"));

        Assert.Equal("こんにちは", result.Text);
        Assert.Contains("ja", provider.Metadata.SupportedLanguages);
        Assert.Contains("de", provider.Metadata.SupportedLanguages);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class StubSecretStore : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

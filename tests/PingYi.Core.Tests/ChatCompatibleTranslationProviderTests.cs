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

    [Theory]
    [InlineData("https://api.openai.com/v1/chat/completions", "https://api.openai.com/v1/models")]
    [InlineData("https://api.deepseek.com/chat/completions", "https://api.deepseek.com/models")]
    [InlineData("https://openrouter.ai/api/v1/chat/completions", "https://openrouter.ai/api/v1/models")]
    [InlineData("https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", "https://dashscope.aliyuncs.com/compatible-mode/v1/models")]
    [InlineData("https://open.bigmodel.cn/api/paas/v4/chat/completions", "https://open.bigmodel.cn/api/paas/v4/models")]
    public async Task RemoteApiModelDiscovery_UsesBearerKeyAndReturnsAllModels(
        string chatEndpoint, string expectedModelsEndpoint)
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(expectedModelsEndpoint, request.RequestUri?.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-api-key", request.Headers.Authorization?.Parameter);
            return Json("{\"object\":\"list\",\"data\":[{\"id\":\"model-a\"},{\"id\":\"model-b\"},{\"id\":\"vision-model\"}]}");
        });
        var settings = new AppSettings
        {
            CustomTranslationEndpoint = chatEndpoint,
            CustomTranslationModel = "model-a"
        };
        var provider = new ChatCompatibleTranslationProvider(
            new HttpClient(handler),
            new StubSecretStore("test-api-key"),
            () => settings);

        var models = await provider.GetAvailableModelsAsync();

        Assert.Equal(["model-a", "model-b", "vision-model"], models);
    }

    [Fact]
    public async Task RemoteApiModelDiscovery_FollowsOpenAiStyleCursorPagination()
    {
        var calls = 0;
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            calls++;
            if (calls == 1)
            {
                Assert.Equal("https://api.openai.com/v1/models", request.RequestUri?.AbsoluteUri);
                return Json("{\"data\":[{\"id\":\"model-a\"}],\"has_more\":true,\"last_id\":\"model-a\"}");
            }

            Assert.Equal("https://api.openai.com/v1/models?limit=100&after=model-a", request.RequestUri?.AbsoluteUri);
            return Json("{\"data\":[{\"id\":\"model-b\"}],\"has_more\":false}");
        });
        var settings = new AppSettings
        {
            CustomTranslationEndpoint = "https://api.openai.com/v1/chat/completions",
            CustomTranslationModel = "model-a"
        };
        var provider = new ChatCompatibleTranslationProvider(
            new HttpClient(handler),
            new StubSecretStore("test-api-key"),
            () => settings);

        var models = await provider.GetAvailableModelsAsync();

        Assert.Equal(2, calls);
        Assert.Equal(["model-a", "model-b"], models);
    }

    [Fact]
    public async Task ResponsesProtocol_UsesResponsesEndpointStoreFalseAndOutputText()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri?.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-api-key", request.Headers.Authorization?.Parameter);
            using var document = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var root = document.RootElement;
            Assert.Equal("gpt-test", root.GetProperty("model").GetString());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.Contains("专业翻译引擎", root.GetProperty("instructions").GetString());
            Assert.Equal("Hello world", root.GetProperty("input").GetString());
            Assert.False(root.TryGetProperty("messages", out _));
            return Json("{\\"id\\":\\"resp_123\\",\\"output_text\\":\\"你好，世界。\\"}");
        });
        var settings = new AppSettings
        {
            CustomTranslationEndpoint = "https://api.openai.com/v1/chat/completions",
            CustomTranslationModel = "gpt-test",
            CustomTranslationApiProtocol = ModelApiProtocols.Responses
        };
        var provider = new ChatCompatibleTranslationProvider(
            new HttpClient(handler), new StubSecretStore("test-api-key"), () => settings);

        var result = await provider.TranslateAsync(new TranslationRequest("Hello world", "en", "zh"));

        Assert.Equal("你好，世界。", result.Text);
    }

    [Fact]
    public async Task ResponsesProtocol_ParsesOutputItemsWhenOutputTextHelperIsAbsent()
    {
        var handler = new StubHandler(_ => Json(
            "{\\"output\\":[{\\"type\\":\\"reasoning\\",\\"summary\\":[]},{\\"type\\":\\"message\\",\\"content\\":[{\\"type\\":\\"output_text\\",\\"text\\":\\"Part one\\"},{\\"type\\":\\"output_text\\",\\"text\\":\\"Part two\\"}]}]}"));
        var settings = new AppSettings
        {
            CustomTranslationEndpoint = "https://api.openai.com/v1/chat/completions",
            CustomTranslationModel = "gpt-test",
            CustomTranslationApiProtocol = ModelApiProtocols.Responses
        };
        var provider = new ChatCompatibleTranslationProvider(new HttpClient(handler), new StubSecretStore(), () => settings);

        var result = await provider.TranslateAsync(new TranslationRequest("Hello", "en", "zh"));

        Assert.Equal("Part one\nPart two", result.Text);
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

    [Fact]
    public async Task RemoteHttpEndpoint_IsRejectedBeforeCredentialsOrTextCanBeSent()
    {
        var requestSent = false;
        var handler = new StubHandler(_ =>
        {
            requestSent = true;
            return Json("{}");
        });
        var settings = new AppSettings
        {
            CustomTranslationEndpoint = "http://api.example.com/v1/chat/completions",
            CustomTranslationModel = "remote-model"
        };
        var provider = new ChatCompatibleTranslationProvider(
            new HttpClient(handler),
            new StubSecretStore(),
            () => settings);

        var availability = await provider.GetAvailabilityAsync();
        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.TranslateAsync(new TranslationRequest("private text", "en", "zh")));

        Assert.False(availability.IsAvailable);
        Assert.Equal("custom_endpoint_insecure_transport", exception.Code);
        Assert.False(requestSent);
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

    private sealed class StubSecretStore(string? apiKey = null) : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(apiKey);

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

using System.Net;
using System.Text;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class ChatTranslationRobustnessTests
{
    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"choices\":null}")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[null]}")]
    [InlineData("{\"choices\":[{\"message\":null}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":null}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":42}}]}")]
    public async Task MalformedTranslation_ReportsStableSchemaError(string response)
    {
        using var client = new HttpClient(new StubHandler(_ => Json(response)));
        var provider = CreateProvider(client);

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.TranslateAsync(new TranslationRequest("Hello", "en", "zh")));

        Assert.Equal("custom_translate_schema", exception.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\\n\\t")]
    public async Task EmptyTranslation_IsNotSuccessful(string text)
    {
        using var client = new HttpClient(new StubHandler(_ =>
            Json("{\"choices\":[{\"message\":{\"content\":\"" + text + "\"}}]}")));
        var provider = CreateProvider(client);

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.TranslateAsync(new TranslationRequest("Hello", "en", "zh")));

        Assert.Equal("custom_translate_empty", exception.Code);
    }

    [Fact]
    public async Task EmptyTranslation_ActuallyInvokesOfflineFallback()
    {
        using var client = new HttpClient(new StubHandler(request => request.Method == HttpMethod.Get
            ? Json("{\"data\":[{\"id\":\"test-model\"}]}")
            : Json("{\"choices\":[{\"message\":{\"content\":\" \"}}]}")));
        var fallback = new OfflineProvider();

        var execution = await TranslationFallback.ExecuteAsync(
            CreateProvider(client), fallback, new TranslationRequest("Hello", "en", "zh"));

        Assert.True(execution.UsedFallback);
        Assert.Equal("离线译文", execution.Result.Text);
        Assert.Equal(1, fallback.Calls);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"data\":[null]}")]
    [InlineData("{\"data\":[{}]}")]
    [InlineData("{\"data\":[{\"id\":123}]}")]
    [InlineData("{\"data\":[{\"id\":\" \"}]}")]
    public async Task MalformedModelList_ReturnsUnavailableInsteadOfThrowing(string response)
    {
        using var client = new HttpClient(new StubHandler(_ => Json(response)));

        var availability = await CreateProvider(client).GetAvailabilityAsync();

        Assert.False(availability.IsAvailable);
        Assert.Contains("格式", availability.Message);
    }

    [Fact]
    public async Task ModelList_RemovesDuplicateIds()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            Json("{\"data\":[{\"id\":\"test-model\"},{\"id\":\"test-model\"}]}")));

        var models = await CreateProvider(client).GetAvailableModelsAsync();

        Assert.Equal("test-model", Assert.Single(models));
    }

    [Fact]
    public async Task ValidTranslation_PreservesInternalParagraphs()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            Json("{\"choices\":[{\"message\":{\"content\":\"  第一段\\n\\n第二段  \"}}]}")));

        var result = await CreateProvider(client).TranslateAsync(new TranslationRequest("Hello", "en", "zh"));

        Assert.Equal("第一段\n\n第二段", result.Text);
    }

    [Fact]
    public async Task MalformedResponse_DoesNotLeakResponseBodyThroughException()
    {
        const string privateMarker = "PRIVATE_OCR_AND_API_KEY";
        using var client = new HttpClient(new StubHandler(_ => Json(privateMarker)));

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            CreateProvider(client).TranslateAsync(new TranslationRequest("Hello", "en", "zh")));

        Assert.DoesNotContain(privateMarker, exception.ToString());
        Assert.Null(exception.InnerException);
    }

    private static ChatCompatibleTranslationProvider CreateProvider(HttpClient client) => new(
        client, new StubSecretStore(), () => new AppSettings { CustomTranslationModel = "test-model" });

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(factory(request));
    }

    private sealed class StubSecretStore : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class OfflineProvider : ITranslationProvider
    {
        public int Calls { get; private set; }
        public ProviderMetadata Metadata { get; } = new(
            "local-argos", "offline", ProviderExecutionLocation.Local,
            UploadsImage: false, RequiresSecret: false, ["en", "zh"]);
        public ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ProviderAvailability.Available);
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new TranslationResult("离线译文", request.SourceLanguage, request.TargetLanguage));
        }
    }
}

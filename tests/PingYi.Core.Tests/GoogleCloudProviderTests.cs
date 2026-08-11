using System.Net;
using System.Text;
using System.Text.Json;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class GoogleCloudProviderTests
{
    [Fact]
    public async Task VisionOcr_UsesHeaderCredentialAndReturnsTextBlocks()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("https://vision.googleapis.com/v1/images:annotate", request.RequestUri?.AbsoluteUri);
            Assert.Equal("secret-google-key", request.Headers.GetValues("x-goog-api-key").Single());
            Assert.DoesNotContain("secret-google-key", request.RequestUri?.AbsoluteUri);
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(payload);
            Assert.Equal(
                "TEXT_DETECTION",
                document.RootElement.GetProperty("requests")[0].GetProperty("features")[0].GetProperty("type").GetString());
            return Json("""
                {"responses":[{"textAnnotations":[
                  {"locale":"fr","description":"Bonjour le monde"},
                  {"description":"Bonjour","boundingPoly":{"vertices":[{"x":10,"y":12},{"x":90,"y":12},{"x":90,"y":35},{"x":10,"y":35}]}},
                  {"description":"le monde","boundingPoly":{"vertices":[{"x":100,"y":12},{"x":190,"y":12},{"x":190,"y":35},{"x":100,"y":35}]}}
                ],"fullTextAnnotation":{"text":"Bonjour le monde\n"}}]}
                """);
        });
        var provider = new GoogleCloudVisionOcrProvider(
            new HttpClient(handler),
            new StubSecretStore("secret-google-key"));

        var result = await provider.RecognizeAsync(
            new ImageFrame([1, 2, 3], 320, 100, new PixelRect(0, 0, 320, 100)),
            new OcrOptions());

        Assert.Equal("Bonjour le monde", result.PlainText);
        Assert.Equal("fr", result.DetectedLanguage);
        Assert.Equal(2, result.Blocks.Count);
        Assert.Equal(new PixelRect(10, 12, 80, 23), result.Blocks[0].Bounds);
        Assert.True(provider.Metadata.UploadsImage);
    }

    [Fact]
    public async Task VisionOcr_MapsTraditionalChineseLanguageHint()
    {
        var handler = new StubHandler(request =>
        {
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(payload);
            Assert.Equal(
                "zh-TW",
                document.RootElement.GetProperty("requests")[0]
                    .GetProperty("imageContext").GetProperty("languageHints")[0].GetString());
            return Json("""{"responses":[{"fullTextAnnotation":{"text":"繁體中文"}}]}""");
        });
        var provider = new GoogleCloudVisionOcrProvider(
            new HttpClient(handler),
            new StubSecretStore("key"));

        var result = await provider.RecognizeAsync(
            new ImageFrame([1], 100, 50, new PixelRect(0, 0, 100, 50)),
            new OcrOptions("zh-Hant"));

        Assert.Equal("繁體中文", result.PlainText);
    }

    [Fact]
    public async Task Translation_AutoDetectsSourceAndDecodesText()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("https://translation.googleapis.com/language/translate/v2", request.RequestUri?.AbsoluteUri);
            Assert.Equal("secret-google-key", request.Headers.GetValues("x-goog-api-key").Single());
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(payload);
            Assert.False(document.RootElement.TryGetProperty("source", out _));
            Assert.Equal("zh-CN", document.RootElement.GetProperty("target").GetString());
            Assert.Equal("text", document.RootElement.GetProperty("format").GetString());
            return Json("""
                {"data":{"translations":[{"translatedText":"你好 &amp; 欢迎","detectedSourceLanguage":"en"}]}}
                """);
        });
        var provider = new GoogleCloudTranslationProvider(
            new HttpClient(handler),
            new StubSecretStore("secret-google-key"));

        var result = await provider.TranslateAsync(
            new TranslationRequest("Hello & welcome", LanguageCatalog.Auto, "zh"));

        Assert.Equal("你好 & 欢迎", result.Text);
        Assert.Equal("en", result.SourceLanguage);
        Assert.Contains("sw", provider.Metadata.SupportedLanguages);
    }

    [Fact]
    public async Task Providers_ReportMissingCredentialWithoutNetworkRequest()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Network must not be used."));
        var secrets = new StubSecretStore(null);
        var ocr = new GoogleCloudVisionOcrProvider(new HttpClient(handler), secrets);
        var translation = new GoogleCloudTranslationProvider(new HttpClient(handler), secrets);

        Assert.False((await ocr.GetAvailabilityAsync()).IsAvailable);
        Assert.False((await translation.GetAvailabilityAsync()).IsAvailable);
    }

    [Fact]
    public async Task VisionCredentialValidation_UsesOnlyBuiltInTestImage()
    {
        var handler = new StubHandler(request =>
        {
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(payload);
            var encodedImage = document.RootElement.GetProperty("requests")[0]
                .GetProperty("image").GetProperty("content").GetString();
            Assert.NotNull(encodedImage);
            Assert.Equal(69, Convert.FromBase64String(encodedImage!).Length);
            return Json("""{"responses":[{}]}""");
        });
        var provider = new GoogleCloudVisionOcrProvider(
            new HttpClient(handler),
            new StubSecretStore("key"));

        await provider.ValidateCredentialsAsync();
    }

    [Fact]
    public async Task Translation_InvalidJson_IsReportedAsProviderError()
    {
        var provider = new GoogleCloudTranslationProvider(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "text/plain")
            })),
            new StubSecretStore("key"));

        var exception = await Assert.ThrowsAsync<ProviderException>(() => provider.TranslateAsync(
            new TranslationRequest("hello", "en", "zh")));

        Assert.Equal("google_translate_schema", exception.Code);
        Assert.DoesNotContain("key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }

    private sealed class StubSecretStore(string? value) : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(value);
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

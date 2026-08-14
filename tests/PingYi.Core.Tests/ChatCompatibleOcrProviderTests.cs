using System.Net;
using System.Text;
using System.Text.Json;
using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;

namespace PingYi.Core.Tests;

public sealed class ChatCompatibleOcrProviderTests
{
    [Fact]
    public async Task Recognize_SendsOpenAiImageUrlAndReturnsUnfencedText()
    {
        var handler = new StubHandler(request =>
        {
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(payload);
            var content = document.RootElement.GetProperty("messages")[1].GetProperty("content");
            Assert.Equal("text", content[0].GetProperty("type").GetString());
            Assert.StartsWith(
                "data:image/png;base64,",
                content[1].GetProperty("image_url").GetProperty("url").GetString());
            return Json("{\"choices\":[{\"message\":{\"content\":\"```text\\nPINGYI OCR 2026\\nsecond line\\n```\"}}]}");
        });
        var provider = CreateProvider(new HttpClient(handler));

        var result = await provider.RecognizeAsync(
            new ImageFrame([1, 2, 3], 320, 100, new PixelRect(0, 0, 320, 100)),
            new OcrOptions("en"));

        Assert.Equal("PINGYI OCR 2026\nsecond line", result.PlainText);
        Assert.Equal("local-vlm-ocr", provider.Metadata.Id);
        Assert.True(provider.Metadata.UploadsImage);
    }

    [Fact]
    public async Task CorrectedMode_IncludesPaddleDraftInVisionPrompt()
    {
        var handler = new StubHandler(request =>
        {
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(payload);
            var prompt = document.RootElement
                .GetProperty("messages")[1]
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString();
            Assert.Contains("PINGV1 OCR", prompt);
            return Json("{\"choices\":[{\"message\":{\"content\":\"PINGYI OCR\"}}]}");
        });
        var draftProvider = new StubOcrProvider("PINGV1 OCR");
        var provider = CreateProvider(new HttpClient(handler), draftProvider);

        var result = await provider.RecognizeAsync(
            new ImageFrame([1], 200, 80, new PixelRect(0, 0, 200, 80)),
            new OcrOptions());

        Assert.Equal("PINGYI OCR", result.PlainText);
        Assert.Equal("local-vlm-corrected", provider.Metadata.Id);
        Assert.Equal(1, draftProvider.RecognizeCalls);
    }

    [Fact]
    public async Task RemoteHttpEndpoint_IsRejectedBeforeImageCanBeSent()
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
        var provider = CreateProvider(new HttpClient(handler), settings: settings);

        var exception = await Assert.ThrowsAsync<ProviderException>(() => provider.RecognizeAsync(
            new ImageFrame([1], 100, 40, new PixelRect(0, 0, 100, 40)),
            new OcrOptions("en")));

        Assert.Equal("custom_endpoint_insecure_transport", exception.Code);
        Assert.False(requestSent);
    }

    [Fact]
    [Trait("Category", "LocalLlamaVision")]
    public async Task LocalLlama_RecognizesRealSyntheticImageWhenEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PINGYI_RUN_LLAMA_TESTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var secrets = new StubSecretStore();
        var settings = new AppSettings();
        var translation = new ChatCompatibleTranslationProvider(httpClient, secrets, () => settings);
        var provider = new ChatCompatibleOcrProvider(httpClient, secrets, () => settings, translation);

        var result = await provider.RecognizeAsync(CreateTestImage(), new OcrOptions("en"));

        Assert.Contains("PINGYI", result.PlainText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026", result.PlainText, StringComparison.Ordinal);
    }

    private static ChatCompatibleOcrProvider CreateProvider(
        HttpClient httpClient,
        IOcrProvider? draft = null,
        AppSettings? settings = null)
    {
        var secrets = new StubSecretStore();
        return new ChatCompatibleOcrProvider(
            httpClient,
            secrets,
            () => settings ?? new AppSettings(),
            new StubTranslationProvider(),
            draft);
    }

    private static ImageFrame CreateTestImage()
    {
        const int width = 360;
        const int height = 96;
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(12, 20, 32));
        using var typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 28);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText("PINGYI OCR 2026", 22, 58, SKTextAlign.Left, font, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new ImageFrame(data.ToArray(), width, height, new PixelRect(0, 0, width, height));
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

    private sealed class StubTranslationProvider : ITranslationProvider
    {
        public ProviderMetadata Metadata { get; } = new(
            "custom-chat", "custom", ProviderExecutionLocation.Local, false, false, LanguageCatalog.Codes);

        public ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ProviderAvailability.Available);

        public Task<TranslationResult> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubOcrProvider(string text) : IOcrProvider
    {
        public int RecognizeCalls { get; private set; }

        public ProviderMetadata Metadata { get; } = new(
            "draft", "draft", ProviderExecutionLocation.Local, false, false, ["zh", "en"]);

        public ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ProviderAvailability.Available);

        public Task<OcrResult> RecognizeAsync(
            ImageFrame image,
            OcrOptions options,
            CancellationToken cancellationToken = default)
        {
            RecognizeCalls++;
            return Task.FromResult(new OcrResult(
                [new OcrBlock(text, new PixelRect(0, 0, image.Width, image.Height), 0.8)],
                text,
                "en"));
        }
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

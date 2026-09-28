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
            using var document = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var content = document.RootElement.GetProperty("messages")[1].GetProperty("content");
            Assert.Equal("text", content[0].GetProperty("type").GetString());
            Assert.StartsWith("data:image/png;base64,", content[1].GetProperty("image_url").GetProperty("url").GetString());
            return Json("{\"choices\":[{\"message\":{\"content\":\"```text\\nPINGYI OCR 2026\\nsecond line\\n```\"}}]}");
        });
        var provider = CreateProvider(new HttpClient(handler));
        var result = await provider.RecognizeAsync(new ImageFrame([1, 2, 3], 320, 100, new PixelRect(0, 0, 320, 100)), new OcrOptions("en"));
        Assert.Equal("PINGYI OCR 2026\nsecond line", result.PlainText);
        Assert.Equal("local-vlm-ocr", provider.Metadata.Id);
        Assert.True(provider.Metadata.UploadsImage);
    }

    [Fact]
    public async Task DirectMode_HasNoDraftAndDoesNotInventConfidence()
    {
        var requests = 0;
        var handler = new StubHandler(request =>
        {
            requests++;
            using var document = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var prompt = document.RootElement.GetProperty("messages")[1].GetProperty("content")[0].GetProperty("text").GetString();
            Assert.DoesNotContain("Paddle", prompt);
            Assert.DoesNotContain("初稿", prompt);
            return Json("{\"choices\":[{\"message\":{\"content\":\"PINGYI OCR\"}}]}");
        });
        var result = await CreateProvider(new HttpClient(handler)).RecognizeAsync(
            new ImageFrame([1], 200, 80, new PixelRect(0, 0, 200, 80)), new OcrOptions());
        Assert.Equal("PINGYI OCR", result.PlainText);
        Assert.Equal(1, requests);
        Assert.Equal(0, Assert.Single(result.Blocks).Confidence);
    }

    [Theory]
    [InlineData("http://api.example.com/v1/chat/completions", "custom_endpoint_insecure_transport")]
    [InlineData("https://user:password@example.com/v1/chat/completions", "vlm_ocr_endpoint_invalid")]
    public async Task UnsafeEndpoint_IsRejectedBeforeImageCanBeSent(string endpoint, string errorCode)
    {
        var sent = false;
        var handler = new StubHandler(_ => { sent = true; return Json("{}"); });
        var provider = CreateProvider(new HttpClient(handler), new AppSettings
            { CustomTranslationEndpoint = endpoint, CustomTranslationModel = "remote-model" });
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RecognizeAsync(
            new ImageFrame([1], 100, 40, new PixelRect(0, 0, 100, 40)), new OcrOptions("en")));
        Assert.Equal(errorCode, error.Code);
        Assert.False(sent);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":42}}]}")]
    public async Task InvalidResponse_IsReportedWithoutReturningProviderContent(string body)
    {
        var provider = CreateProvider(new HttpClient(new StubHandler(_ => Json(body))));
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.RecognizeAsync(
            new ImageFrame([1], 100, 40, new PixelRect(0, 0, 100, 40)), new OcrOptions()));
        Assert.Equal("vlm_ocr_schema", error.Code);
    }

    [Fact]
    [Trait("Category", "LocalLlamaVision")]
    public async Task LocalLlama_RecognizesRealSyntheticImageWhenEnabled()
    {
        if (Environment.GetEnvironmentVariable("PINGYI_RUN_LLAMA_TESTS") != "1") return;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var secrets = new StubSecretStore();
        var settings = new AppSettings();
        var translation = new ChatCompatibleTranslationProvider(client, secrets, () => settings);
        var provider = new ChatCompatibleOcrProvider(client, secrets, () => settings, translation);
        var result = await provider.RecognizeAsync(CreateTestImage(), new OcrOptions("en"));
        Assert.Contains("PINGYI", result.PlainText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026", result.PlainText, StringComparison.Ordinal);
    }

    private static ChatCompatibleOcrProvider CreateProvider(HttpClient client, AppSettings? settings = null) =>
        new(client, new StubSecretStore(), () => settings ?? new AppSettings(), new StubTranslationProvider());
    private static ImageFrame CreateTestImage()
    {
        using var bitmap = new SKBitmap(360, 96);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(12, 20, 32));
        using var typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 28);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText("PINGYI OCR 2026", 22, 58, SKTextAlign.Left, font, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new ImageFrame(data.ToArray(), 360, 96, new PixelRect(0, 0, 360, 96));
    }
    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
        { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(factory(request));
    }
    private sealed class StubTranslationProvider : ITranslationProvider
    {
        public ProviderMetadata Metadata { get; } = new("custom-chat", "custom", ProviderExecutionLocation.Local, false, false, LanguageCatalog.Codes);
        public ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken token = default) => ValueTask.FromResult(ProviderAvailability.Available);
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class StubSecretStore : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string value, CancellationToken token = default) => Task.CompletedTask;
        public Task DeleteAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }
}

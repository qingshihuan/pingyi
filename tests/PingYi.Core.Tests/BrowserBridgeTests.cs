using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class BrowserBridgeTests
{
    private sealed class Translator(ProviderExecutionLocation location = ProviderExecutionLocation.Local) : ITranslationProvider
    {
        public int Calls;
        public TranslationRequest? Request;
        public ProviderMetadata Metadata { get; } = new("local-argos", "Test translator", location, false, false, ["zh", "en"]);
        public ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(ProviderAvailability.Available);
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++; Request = request;
            return Task.FromResult(new TranslationResult("你好，世界。", request.SourceLanguage, request.TargetLanguage));
        }
    }
    private sealed class Ocr(ProviderExecutionLocation location = ProviderExecutionLocation.Local) : IOcrProvider
    {
        public int Calls;
        public ProviderMetadata Metadata { get; } = new("local-paddle", "Test OCR", location, true, false, ["en"]);
        public ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(ProviderAvailability.Available);
        public Task<OcrResult> RecognizeAsync(ImageFrame image, OcrOptions options, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new OcrResult([], "Hello world", "en")); }
    }
    private static BrowserTranslationService Service(Translator translator, Ocr? ocr = null) =>
        new(new ProviderRegistry([ocr ?? new Ocr()], [translator]), _ => Task.CompletedTask);
    private static BrowserRequest Request(BrowserTranslationService service, AppSettings settings) => new()
        { Operation = "translate", Text = "Hello world", RouteKey = service.GetStatus(settings).RouteKey };

    [Fact]
    public async Task OfflineRouteDetectsLanguageAndReusesConfiguredProvider()
    {
        var translator = new Translator(); var service = Service(translator); var settings = new AppSettings();
        var response = await service.HandleAsync(Request(service, settings), settings, default);
        Assert.True(response.Ok); Assert.Equal("你好，世界。", response.Translation);
        Assert.Equal("en", translator.Request!.SourceLanguage); Assert.Equal("zh", translator.Request.TargetLanguage);
        Assert.False(service.GetStatus(settings).RemoteText);
    }

    [Fact]
    public async Task CloudRequiresConsentAndChangingEndpointInvalidatesConsent()
    {
        var translator = new Translator(ProviderExecutionLocation.Cloud); var service = Service(translator); var settings = new AppSettings();
        var request = Request(service, settings);
        Assert.Equal("remote_text_consent_required", (await service.HandleAsync(request, settings, default)).Error);
        Assert.Equal(0, translator.Calls);
        Assert.True((await service.HandleAsync(request with { AllowRemoteText = true }, settings, default)).Ok);
        Assert.Equal("settings_changed", (await service.HandleAsync(request with { AllowRemoteText = true },
            settings with { CustomTranslationEndpoint = "https://example.org/v1/chat/completions" }, default)).Error);
        Assert.Equal(1, translator.Calls);
    }

    [Fact]
    public async Task ImageConsentIsSeparateAndCheckedBeforeDecodingOrProviderCalls()
    {
        var translator = new Translator(); var ocr = new Ocr(ProviderExecutionLocation.Cloud); var service = Service(translator, ocr); var settings = new AppSettings();
        var request = Request(service, settings) with { Operation = "ocr", Image = "not an image", AllowRemoteText = true };
        Assert.Equal("remote_image_consent_required", (await service.HandleAsync(request, settings, default)).Error);
        Assert.Equal("invalid_image", (await service.HandleAsync(request with { AllowRemoteImage = true }, settings, default)).Error);
        Assert.Equal(0, ocr.Calls); Assert.Equal(0, translator.Calls);
    }

    [Fact]
    public async Task ScreenshotUsesConfiguredOcrThenTranslation()
    {
        using var bitmap = new SkiaSharp.SKBitmap(20, 10);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        var translator = new Translator(); var ocr = new Ocr(); var service = Service(translator, ocr); var settings = new AppSettings();
        var response = await service.HandleAsync(Request(service, settings) with
            { Operation = "ocr", Image = Convert.ToBase64String(png.ToArray()) }, settings, default);
        Assert.True(response.Ok); Assert.Equal(1, ocr.Calls); Assert.Equal(1, translator.Calls);
        Assert.Equal("Hello world", response.Original);
    }

    [Fact]
    public async Task RejectsOversizedTextAndInvalidLanguageBeforeInference()
    {
        var translator = new Translator(); var service = Service(translator); var settings = new AppSettings();
        Assert.Equal("invalid_text", (await service.HandleAsync(Request(service, settings) with { Text = new('a', 12001) }, settings, default)).Error);
        Assert.Equal("invalid_language", (await service.HandleAsync(Request(service, settings) with { TargetLanguage = "invalid" }, settings, default)).Error);
        Assert.Equal(0, translator.Calls);
    }

    [Fact]
    public async Task SameLanguageDoesNotCallTranslationService()
    {
        var translator = new Translator(); var service = Service(translator); var settings = new AppSettings();
        var result = await service.HandleAsync(Request(service, settings) with { TargetLanguage = "en" }, settings, default);
        Assert.True(result.Ok); Assert.Equal("Hello world", result.Translation); Assert.Equal(0, translator.Calls);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/v1/chat/completions", false)]
    [InlineData("http://[::1]:8080/v1/chat/completions", false)]
    [InlineData("https://example.org/v1/chat/completions", true)]
    public void CustomProviderRemoteClassificationFollowsConfiguredEndpoint(string endpoint, bool remote)
    {
        var service = Service(new Translator(ProviderExecutionLocation.Configurable));
        Assert.Equal(remote, service.GetStatus(new AppSettings { CustomTranslationEndpoint = endpoint }).RemoteText);
    }

    [Fact]
    public async Task FramingSupportsUnicodeAndRejectsTruncationAndOversizedFrames()
    {
        using var stream = new MemoryStream(); var body = System.Text.Encoding.UTF8.GetBytes("合成测试");
        await BrowserWire.WriteAsync(stream, body, 100, default); stream.Position = 0;
        Assert.Equal(body, await BrowserWire.ReadAsync(stream, 100, default));
        Assert.Null(await BrowserWire.ReadAsync(stream, 100, default));
        await Assert.ThrowsAsync<EndOfStreamException>(() => BrowserWire.ReadAsync(new MemoryStream([4, 0]), 100, default));
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, int.MaxValue);
        await Assert.ThrowsAsync<InvalidDataException>(() => BrowserWire.ReadAsync(new MemoryStream(header), 100, default));
    }

    [Fact]
    public async Task NamedPipeRunsRealProtocolAndNeverReturnsExceptionContent()
    {
        var name = "pingyi-test-" + Guid.NewGuid().ToString("N");
        await using var server = new BrowserBridgeServer(name, (_, _) => throw new Exception("PRIVATE_SYNTHETIC_CONTENT"));
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(deadline.Token);
        await BrowserWire.WriteAsync(client, JsonSerializer.SerializeToUtf8Bytes(new BrowserRequest(), BrowserJsonContext.Default.BrowserRequest), BrowserWire.MaxRequestBytes, deadline.Token);
        var reply = await BrowserWire.ReadAsync(client, BrowserWire.MaxResponseBytes, deadline.Token);
        var response = JsonSerializer.Deserialize(reply!, BrowserJsonContext.Default.BrowserResponse)!;
        Assert.False(response.Ok); Assert.Equal("provider_failed", response.Error);
        Assert.DoesNotContain("PRIVATE", System.Text.Encoding.UTF8.GetString(reply!));
    }
}

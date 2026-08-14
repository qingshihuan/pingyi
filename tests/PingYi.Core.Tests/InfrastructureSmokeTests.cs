using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;

namespace PingYi.Core.Tests;

public sealed class InfrastructureSmokeTests
{
    [Fact]
    public void GlobalHotkeyGesture_ParsesCtrlAltD()
    {
        var gesture = GlobalHotkeyGesture.Parse("Ctrl+Alt+D");

        Assert.True(gesture.Control);
        Assert.True(gesture.Alt);
        Assert.False(gesture.Shift);
        Assert.Equal('D', gesture.Key);
    }

    [Fact]
    [Trait("Category", "WindowsHotkey")]
    public async Task WindowsGlobalHotkey_RegistersCtrlAltDWhenEnabled()
    {
        if (!OperatingSystem.IsWindows() ||
            !string.Equals(Environment.GetEnvironmentVariable("PINGYI_RUN_HOTKEY_TESTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        await using var service = GlobalHotkeyServiceFactory.Create();
        await service.StartAsync("Ctrl+Alt+D");
        await service.StopAsync();
    }

    [Fact]
    public async Task EngineHost_AnswersHealthRequestWithoutLoadingModels()
    {
        await using var client = new EngineProcessClient(new AppDataPaths());

        var result = await client.CallAsync("health");

        Assert.True(result.TryGetProperty("paddleocr", out _));
        Assert.True(result.TryGetProperty("argos", out _));
        Assert.True(result.TryGetProperty("translationModelsReady", out _));
    }

    [Fact]
    public async Task EngineHost_HonorsPreCanceledRequest()
    {
        await using var client = new EngineProcessClient(new AppDataPaths());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CallAsync("health", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task EngineHost_ReportsRequestTimeoutWithStableErrorCode()
    {
        await using var client = new EngineProcessClient(
            new AppDataPaths(),
            TimeSpan.FromTicks(1));

        var exception = await Assert.ThrowsAsync<ProviderException>(
            () => client.CallAsync("health"));

        Assert.Equal("engine_timeout", exception.Code);
    }

    [Fact]
    public async Task EngineHost_CanceledInFlightRequestDoesNotPoisonNextRequest()
    {
        await using var client = new EngineProcessClient(new AppDataPaths());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CallAsync("health", cancellationToken: cancellation.Token));

        var result = await client.CallAsync("health");
        Assert.True(result.TryGetProperty("paddleocr", out _));
    }

    [Fact]
    public async Task ManagedModelService_DisposeCancelsOperationWaitingForGate()
    {
        var service = new ManagedModelService(new AppDataPaths());
        var gate = Assert.IsType<SemaphoreSlim>(
            typeof(ManagedModelService)
                .GetField("_operationGate", System.Reflection.BindingFlags.Instance |
                                             System.Reflection.BindingFlags.NonPublic)!
                .GetValue(service));
        await gate.WaitAsync();

        var pendingOperation = service.StopAsync();
        var dispose = service.DisposeAsync().AsTask();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingOperation);
        }
        finally
        {
            gate.Release();
        }

        await dispose.WaitAsync(TimeSpan.FromSeconds(2));
        await service.DisposeAsync();
    }

    [Fact]
    public async Task PaddleOcrProvider_DisposeWaitsForActiveNativeInferenceGate()
    {
        var provider = new PaddleOcrProvider(new AppDataPaths());
        var gate = Assert.IsType<SemaphoreSlim>(
            typeof(PaddleOcrProvider)
                .GetField("_inferenceGate", System.Reflection.BindingFlags.Instance |
                                            System.Reflection.BindingFlags.NonPublic)!
                .GetValue(provider));
        await gate.WaitAsync();

        var dispose = provider.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);

        gate.Release();
        await dispose.WaitAsync(TimeSpan.FromSeconds(2));
        await provider.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.RecognizeAsync(
                new ImageFrame([], 0, 0, new PixelRect(0, 0, 0, 0)),
                new OcrOptions("auto")));
    }

    [Fact]
    public async Task ScreenCaptureAndCrop_ReturnValidInMemoryPng()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var capture = await ScreenCaptureServiceFactory.Create().CaptureDesktopAsync();
        Assert.True(capture.Width > 0);
        Assert.True(capture.Height > 0);
        Assert.True(capture.PngBytes.Length > 8);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, capture.PngBytes[..4]);

        var cropWidth = Math.Min(64, capture.Width);
        var cropHeight = Math.Min(64, capture.Height);
        var cropped = new SkiaImageCropper().Crop(capture, new PixelRect(0, 0, cropWidth, cropHeight));
        Assert.Equal(cropWidth, cropped.Width);
        Assert.Equal(cropHeight, cropped.Height);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, cropped.PngBytes[..4]);
    }

    [Fact]
    [Trait("Category", "LocalModels")]
    public async Task LocalModels_RecognizeAndTranslateFixedBilingualImage()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("PINGYI_RUN_MODEL_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        using var bitmap = new SKBitmap(1000, 260);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        using var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 64);
        canvas.DrawText("PingYi OCR 2026", 38, 90, SKTextAlign.Left, font, paint);
        canvas.DrawText("屏幕截图翻译", 38, 200, SKTextAlign.Left, font, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var frame = new ImageFrame(
            encoded.ToArray(),
            bitmap.Width,
            bitmap.Height,
            new PixelRect(0, 0, bitmap.Width, bitmap.Height));

        await using var engine = new EngineProcessClient(new AppDataPaths());
        using var ocrProvider = new PaddleOcrProvider(new AppDataPaths());
        var ocr = await ocrProvider.RecognizeAsync(frame, new OcrOptions("auto"));

        var expected = "PingYi OCR 2026屏幕截图翻译";
        var similarity = NormalizedSimilarity(expected, ocr.PlainText);
        Assert.True(
            similarity >= 0.95,
            $"OCR normalized similarity was {similarity:P2}. Actual: {ocr.PlainText}");

        var translation = await new ArgosTranslationProvider(engine).TranslateAsync(
            new TranslationRequest("屏幕截图翻译", "zh", "en"));
        Assert.False(string.IsNullOrWhiteSpace(translation.Text));
    }

    private static double NormalizedSimilarity(string expected, string actual)
    {
        static string Normalize(string value) => new(
            value.Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

        var left = Normalize(expected);
        var right = Normalize(actual);
        if (left.Length == 0 && right.Length == 0)
        {
            return 1;
        }

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return 1d - (double)previous[right.Length] / Math.Max(left.Length, right.Length);
    }
}

using PingYi.Core;
namespace PingYi.Core.Tests;

public sealed class OcrRetryCacheTests
{
    [Fact]
    public void Failed_translation_reuses_ocr_but_success_and_changed_recognizer_do_not()
    {
        var cache = new OcrRetryCache();
        var image = new ImageFrame([1], 1, 1, new(0, 0, 1, 1));
        var settings = new AppSettings { OcrProviderId = "local-vlm-ocr", CustomTranslationModel = "model-a" };
        var result = new OcrResult([], "synthetic", "en");
        cache.Remember(image, settings, result, "vision");
        Assert.True(cache.TryGetForTranslationRetry(image, settings with { TargetLanguage = "ja" }, out var reused, out var label));
        Assert.Same(result, reused); Assert.Equal("vision", label);
        Assert.False(cache.TryGetForTranslationRetry(image, settings with { CustomTranslationModel = "model-b" }, out _, out _));
        Assert.False(cache.TryGetForTranslationRetry(image with { PngBytes = [2] }, settings, out _, out _));
        cache.MarkTranslationComplete(image);
        Assert.False(cache.TryGetForTranslationRetry(image, settings, out _, out _));
    }
}

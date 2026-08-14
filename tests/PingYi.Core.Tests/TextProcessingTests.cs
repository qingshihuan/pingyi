using PingYi.Core;

namespace PingYi.Core.Tests;

public sealed class TextProcessingTests
{
    [Theory]
    [InlineData("这是中文界面", "zh")]
    [InlineData("Screenshot translation", "en")]
    [InlineData("Bonjour le monde, merci beaucoup", "fr")]
    [InlineData("Hola, ¿cómo estás? Muchas gracias", "es")]
    [InlineData("Guten Morgen, wie geht es Ihnen?", "de")]
    [InlineData("版本 version", "zh")]
    [InlineData("画面を翻訳します", "ja")]
    [InlineData("화면 번역", "ko")]
    [InlineData("Перевод экрана", "ru")]
    [InlineData("ترجمة الشاشة", "ar")]
    [InlineData("स्क्रीन अनुवाद", "hi")]
    [InlineData("แปลหน้าจอ", "th")]
    [InlineData("", "unknown")]
    public void DetectLanguage_ReturnsExpectedLanguage(string text, string expected)
    {
        Assert.Equal(expected, TextProcessing.DetectLanguage(text));
    }

    [Theory]
    [InlineData("Hello, open the screenshot settings", "en")]
    [InlineData("PingYi", "unknown")]
    [InlineData("Bonjour le monde", "fr")]
    [InlineData("Hola, ¿cómo estás?", "es")]
    public void DetectLanguageForOfflineFallback_IsConservativeForAmbiguousLatinText(
        string text,
        string expected)
    {
        Assert.Equal(expected, TextProcessing.DetectLanguageForOfflineFallback(text));
    }

    [Theory]
    [InlineData("zh", "auto-opposite", "en")]
    [InlineData("zh-Hant", "auto-opposite", "en")]
    [InlineData("en", "auto-opposite", "zh")]
    [InlineData("zh", "zh", "zh")]
    [InlineData("auto", "ja", "ja")]
    [InlineData("es", "de", "de")]
    public void ResolveTargetLanguage_RespectsExplicitAndOppositeTargets(
        string source,
        string configured,
        string expected)
    {
        Assert.Equal(expected, TextProcessing.ResolveTargetLanguage(source, configured));
    }

    [Theory]
    [InlineData("auto", "auto-opposite", "ja", "画面を翻訳します", true, "auto", "zh")]
    [InlineData("auto", "auto-opposite", "zh", "这是中文", true, "auto", "en")]
    [InlineData("auto", "auto-opposite", "en", "Hello", false, "en", "zh")]
    [InlineData("ja", "zh", "en", "Hello", true, "ja", "zh")]
    public void ResolveTranslationLanguages_SeparatesAutomaticSourceDetectionFromTargetRule(
        string configuredSource,
        string configuredTarget,
        string detectedOcrLanguage,
        string text,
        bool providerCanDetect,
        string expectedSource,
        string expectedTarget)
    {
        var route = TextProcessing.ResolveTranslationLanguages(
            configuredSource,
            configuredTarget,
            detectedOcrLanguage,
            text,
            providerCanDetect);

        Assert.Equal(expectedSource, route.SourceLanguage);
        Assert.Equal(expectedTarget, route.TargetLanguage);
    }

    [Fact]
    public void BuildPlainText_SortsBlocksIntoVisualReadingOrder()
    {
        OcrBlock[] blocks =
        [
            new("world", new PixelRect(80, 10, 50, 20), 0.9),
            new("second line", new PixelRect(10, 50, 120, 20), 0.9),
            new("Hello", new PixelRect(10, 12, 55, 18), 0.9)
        ];

        Assert.Equal($"Hello world{Environment.NewLine}second line", TextProcessing.BuildPlainText(blocks));
    }

    [Fact]
    public void Redact_DoesNotExposeMiddleOfSecret()
    {
        Assert.Equal("ab******yz", TextProcessing.Redact("ab123456yz"));
        Assert.Equal("****", TextProcessing.Redact("abcd"));
    }
}

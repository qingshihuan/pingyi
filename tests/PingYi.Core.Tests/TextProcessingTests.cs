using PingYi.Core;

namespace PingYi.Core.Tests;

public sealed class TextProcessingTests
{
    [Theory]
    [InlineData("这是中文界面", "zh")]
    [InlineData("Screenshot translation", "en")]
    [InlineData("版本 version", "zh")]
    [InlineData("", "unknown")]
    public void DetectLanguage_ReturnsExpectedLanguage(string text, string expected)
    {
        Assert.Equal(expected, TextProcessing.DetectLanguage(text));
    }

    [Theory]
    [InlineData("zh", "auto-opposite", "en")]
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

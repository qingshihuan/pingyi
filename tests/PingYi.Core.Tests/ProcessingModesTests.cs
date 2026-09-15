using PingYi.Core;

namespace PingYi.Core.Tests;

public class ProcessingModesTests
{
    [Theory]
    [InlineData("offline", "local-paddle", "local-argos")]
    [InlineData("llm", "local-paddle", "custom-chat")]
    [InlineData("vision", "local-vlm-corrected", "custom-chat")]
    [InlineData("baidu", "baidu-ocr", "baidu-translate")]
    public void Applying_a_mode_uses_the_documented_providers_without_overwriting_the_configured_endpoint(string id, string ocr, string translation)
    {
        var initial = new AppSettings { CustomTranslationEndpoint = "https://example.com/v1/chat/completions",
            CustomTranslationModel = "my-model", Hotkey = "Ctrl+Alt+G", UiLanguage = "en-US", TargetLanguage = "ja" };
        var settings = ProcessingModes.Apply(initial, id);
        Assert.Equal(ocr, settings.OcrProviderId);
        Assert.Equal(translation, settings.TranslationProviderId);
        Assert.Equal(initial.CustomTranslationEndpoint, settings.CustomTranslationEndpoint);
        Assert.Equal(initial.CustomTranslationModel, settings.CustomTranslationModel);
        Assert.Equal(initial.Hotkey, settings.Hotkey);
        Assert.Equal(initial.TargetLanguage, settings.TargetLanguage);
        Assert.Equal(id, ProcessingModes.Match(settings).Id);
    }
    [Fact]
    public void Custom_mode_is_navigation_only_and_does_not_replace_Google_or_direct_vision_routing()
    {
        var initial = new AppSettings { OcrProviderId = "google-vision-ocr", TranslationProviderId = "google-translate" };
        Assert.Same(initial, ProcessingModes.Apply(initial, "custom"));
        Assert.Equal("custom", ProcessingModes.Match(initial).Id);
        Assert.Equal("custom", ProcessingModes.Match(initial with { OcrProviderId = "local-vlm-ocr" }).Id);
        Assert.Throws<ArgumentException>(() => ProcessingModes.Apply(initial, "made-up"));
    }
}

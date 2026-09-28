using PingYi.Core;

namespace PingYi.Core.Tests;

public class ProcessingModesTests
{
    [Theory]
    [InlineData("lite", "local-paddle", "local-argos")]
    [InlineData("llm", "local-paddle", "custom-chat")]
    [InlineData("baidu", "baidu-ocr", "baidu-translate")]
    public void Applying_a_mode_preserves_custom_settings(string id, string ocr, string translation)
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
        Assert.Equal(initial.UiLanguage, settings.UiLanguage);
        Assert.Equal(id, ProcessingModes.Match(settings).Id);
    }
    [Fact]
    public void Basic_is_direct_local_vision_and_translation_while_offline_maps_to_lightweight()
    {
        var original = new AppSettings { CustomTranslationEndpoint = "http://127.0.0.1:1234/v1/chat/completions", CustomTranslationModel = "vision-model" };
        var basic = ProcessingModes.Apply(original, "basic");
        Assert.Equal("local-vlm-ocr", basic.OcrProviderId);
        Assert.Equal("custom-chat", basic.TranslationProviderId);
        Assert.Equal("basic", ProcessingModes.Match(basic).Id);
        Assert.Equal(original.CustomTranslationEndpoint, basic.CustomTranslationEndpoint);
        Assert.Equal(original.CustomTranslationModel, basic.CustomTranslationModel);
        Assert.Equal("lite", ProcessingModes.Match(ProcessingModes.Apply(original, "offline")).Id);
        Assert.DoesNotContain(ProcessingModes.All, mode => mode.Id == "vision" || mode.OcrProviderId == "local-vlm-corrected");
        Assert.Throws<ArgumentException>(() => ProcessingModes.Apply(original, "vision"));
    }
    [Fact]
    public void Basic_does_not_silently_relabel_a_remote_service_as_local()
    {
        var remote = new AppSettings { CustomTranslationEndpoint = "https://example.com/v1/chat/completions" };
        Assert.Equal("custom", ProcessingModes.Match(remote).Id);
        Assert.Throws<ProviderException>(() => ProcessingModes.Apply(remote, "basic"));
    }
    [Fact]
    public void Custom_mode_is_navigation_only_and_preserves_Google_routing()
    {
        var initial = new AppSettings { OcrProviderId = "google-vision-ocr", TranslationProviderId = "google-translate" };
        Assert.Same(initial, ProcessingModes.Apply(initial, "custom"));
        Assert.Equal("custom", ProcessingModes.Match(initial).Id);
        Assert.Equal("custom", ProcessingModes.Match(initial with { OcrProviderId = "local-vlm-ocr" }).Id);
        Assert.Throws<ArgumentException>(() => ProcessingModes.Apply(initial, "made-up"));
    }
}

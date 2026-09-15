using System.Text.Json;
using PingYi.Core;
using PingYi.Infrastructure;
using Xunit;

namespace PingYi.Core.Tests;

public class ProcessingModesTests
{
    [Theory]
    [InlineData(ProcessingModes.Offline, "local-paddle", "local-argos")]
    [InlineData(ProcessingModes.LocalTranslation, "local-paddle", "custom-chat")]
    [InlineData(ProcessingModes.VisionCorrection, "local-vlm-corrected", "custom-chat")]
    [InlineData(ProcessingModes.Baidu, "baidu-ocr", "baidu-translate")]
    [InlineData(ProcessingModes.Google, "google-vision-ocr", "google-translate")]
    public void Each_named_scheme_routes_to_the_advertised_providers(string mode, string ocr, string translation)
    {
        var settings = ProcessingModes.Apply(new AppSettings(), mode);
        Assert.Equal(ocr, settings.OcrProviderId);
        Assert.Equal(translation, settings.TranslationProviderId);
        Assert.Equal(mode, ProcessingModes.Identify(settings));
    }

    [Fact]
    public void Remote_custom_endpoint_is_never_labeled_local()
    {
        var settings = new AppSettings { TranslationProviderId = "custom-chat", CustomTranslationEndpoint = "https://example.com/v1/chat/completions" };
        Assert.Equal(ProcessingModes.Custom, ProcessingModes.Identify(settings));
        Assert.Equal(settings.Normalize(), ProcessingModes.Apply(settings, ProcessingModes.Custom));
    }

    [Fact]
    public void Offline_mode_does_not_leave_an_unsupported_target_selected()
    {
        var original = new AppSettings { TargetLanguage = "ja", Hotkey = "Ctrl+Alt+Q", UiLanguage = "en-US", CustomTranslationModel = "my-model" };
        var updated = ProcessingModes.Apply(original, ProcessingModes.Offline);
        Assert.Equal(LanguageCatalog.AutoOpposite, updated.TargetLanguage);
        Assert.Equal(original.Hotkey, updated.Hotkey);
        Assert.Equal(original.UiLanguage, updated.UiLanguage);
        Assert.Equal(original.CustomTranslationModel, updated.CustomTranslationModel);
    }

    [Fact]
    public async Task Loading_legacy_classic_settings_preserves_user_configuration_but_does_not_write_variant_back()
    {
        var path = Path.Combine(Path.GetTempPath(), "pingyi-legacy-" + Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, """{"schemaVersion":7,"interfaceStyle":"classic","uiLanguage":"en-US","hotkey":"Ctrl+Alt+Q","customTranslationModel":"my-model"}""");
            var store = new JsonSettingsStore(path);
            var settings = await store.LoadAsync();
            Assert.Equal("modern", settings.InterfaceStyle);
            Assert.Equal("en-US", settings.UiLanguage);
            Assert.Equal("my-model", settings.CustomTranslationModel);
            await store.SaveAsync(settings);
            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("interfaceStyle", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Ctrl+Alt+Q", (await store.LoadAsync()).Hotkey);
        }
        finally { File.Delete(path); }
    }
}

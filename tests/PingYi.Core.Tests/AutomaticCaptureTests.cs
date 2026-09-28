using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class AutomaticCaptureTests
{
    private static OcrResult Text(string text, double confidence = .95, PixelRect? bounds = null) =>
        new([new OcrBlock(text, bounds ?? new PixelRect(10, 10, 150, 30), confidence)], text, "en");
    private static readonly OcrResult Empty = new([], "", "unknown");

    [Fact]
    public void Clear_text_uses_translation_without_assuming_a_target_language()
    {
        var decision = AutomaticCapture.Decide(600, 400, 0, Text("Hello this is an application"), true);
        Assert.Equal(CapturePurpose.TranslateText, decision.Purpose);
        Assert.Equal(CaptureDecisionReason.ReadableText, decision.Reason);
        Assert.Equal(CapturePurpose.TranslateText, AutomaticCapture.Decide(600, 400, 0, Text("这是需要翻译的一段中文说明文字"), true).Purpose);
    }
    [Fact]
    public void Short_tightly_cropped_text_can_be_translated() =>
        Assert.Equal(CapturePurpose.TranslateText, AutomaticCapture.Decide(100, 50, 0, Text("OK", bounds: new PixelRect(5, 5, 75, 30)), true).Purpose);
    [Fact]
    public void Code_and_text_require_choice_instead_of_discarding_either()
    {
        var decision = AutomaticCapture.Decide(600, 400, 2, Text("Visit our website for more information"), true);
        Assert.True(decision.RequiresChoice);
        Assert.Equal(CaptureDecisionReason.MixedContent, decision.Reason);
        Assert.Equal(2, decision.QrCount);
    }
    [Fact]
    public void A_decoded_code_without_substantial_text_can_be_shown_offline() =>
        Assert.Equal(CapturePurpose.DecodeQrCode, AutomaticCapture.Decide(300, 300, 1, Empty, true).Purpose);
    [Fact]
    public void Completed_empty_probe_recommends_description_but_probe_failure_does_not()
    {
        Assert.Equal(CapturePurpose.DescribeImage, AutomaticCapture.Decide(300, 200, 0, Empty, true).Purpose);
        Assert.True(AutomaticCapture.Decide(300, 200, 0, null, false).RequiresChoice);
        Assert.True(AutomaticCapture.Decide(300, 200, 0, Empty, false).RequiresChoice);
    }
    [Theory]
    [InlineData("1234567890", .99)]
    [InlineData("www", .99)]
    [InlineData("Many words in low quality OCR", .20)]
    [InlineData("Some invalid score from a provider", double.NaN)]
    public void Weak_or_nonlinguistic_evidence_stays_uncertain(string text, double confidence) =>
        Assert.True(AutomaticCapture.Decide(1000, 800, 0, Text(text, confidence), true).RequiresChoice);
    [Fact]
    public void New_install_defaults_and_legacy_configuration_are_distinct()
    {
        var fresh = new AppSettings();
        Assert.Equal("basic", ProcessingModes.Match(fresh).Id);
        Assert.True(fresh.AutomaticCaptureEnabled);
        Assert.False(fresh.InitialSetupCompleted);
        var old = new AppSettings { SchemaVersion = 10, OcrProviderId = "local-vlm-corrected",
            TranslationProviderId = "custom-chat", CustomTranslationEndpoint = "https://example.com/v1/chat/completions",
            CustomTranslationModel = "kept-model", Hotkey = "Ctrl+Alt+G" }.Normalize();
        Assert.Equal("local-vlm-ocr", old.OcrProviderId);
        Assert.Equal("kept-model", old.CustomTranslationModel);
        Assert.Equal("https://example.com/v1/chat/completions", old.CustomTranslationEndpoint);
        Assert.Equal("Ctrl+Alt+G", old.Hotkey);
        Assert.True(old.InitialSetupCompleted);
        Assert.Equal(old, old.Normalize());
    }
    [Fact]
    public async Task Missing_file_starts_setup_but_partial_legacy_file_keeps_lightweight()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-auto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "settings.json");
            var store = new JsonSettingsStore(file);
            Assert.False((await store.LoadAsync()).InitialSetupCompleted);
            await File.WriteAllTextAsync(file, "{\"schemaVersion\":9,\"uiLanguage\":\"en-US\"}");
            var old = await store.LoadAsync();
            Assert.Equal("lite", ProcessingModes.Match(old).Id);
            Assert.True(old.InitialSetupCompleted);
            Assert.Equal("en-US", old.UiLanguage);
            await store.SaveAsync(old with { AutomaticCaptureEnabled = false });
            Assert.False((await store.LoadAsync()).AutomaticCaptureEnabled);
        }
        finally { Directory.Delete(directory, true); }
    }
}

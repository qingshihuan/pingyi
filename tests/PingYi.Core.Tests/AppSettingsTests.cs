using PingYi.Core;

namespace PingYi.Core.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Normalize_RepairsInvalidSettingsWithoutSecrets()
    {
        var settings = new AppSettings
        {
            SchemaVersion = 0,
            Hotkey = " ",
            OcrProviderId = "",
            TranslationProviderId = "",
            SourceLanguage = "not-a-language",
            TargetLanguage = "xx",
            CustomTranslationEndpoint = "not a uri",
            CustomTranslationModel = "  model-name  ",
            InterfaceStyle = "unknown"
        };

        var normalized = settings.Normalize();

        Assert.Equal(AppSettings.CurrentSchemaVersion, normalized.SchemaVersion);
        Assert.Equal(AppSettings.DefaultHotkey, normalized.Hotkey);
        Assert.Equal("local-paddle", normalized.OcrProviderId);
        Assert.Equal("local-argos", normalized.TranslationProviderId);
        Assert.Equal("auto", normalized.SourceLanguage);
        Assert.Equal("auto-opposite", normalized.TargetLanguage);
        Assert.Equal("model-name", normalized.CustomTranslationModel);
        Assert.Equal("modern", normalized.InterfaceStyle);
        Assert.True(Uri.TryCreate(normalized.CustomTranslationEndpoint, UriKind.Absolute, out _));
    }

    [Theory]
    [InlineData("ja", "ja")]
    [InlineData("ZH-hant", "zh-Hant")]
    public void Normalize_PreservesKnownMultilingualLanguageCodes(string value, string expected)
    {
        var normalized = new AppSettings
        {
            SourceLanguage = value,
            TargetLanguage = value
        }.Normalize();

        Assert.Equal(expected, normalized.SourceLanguage);
        Assert.Equal(expected, normalized.TargetLanguage);
    }

    [Theory]
    [InlineData("modern", "modern")]
    [InlineData("classic", "classic")]
    [InlineData("", "modern")]
    public void Normalize_PreservesOnlySupportedInterfaceStyles(string value, string expected)
    {
        var normalized = new AppSettings { InterfaceStyle = value }.Normalize();

        Assert.Equal(expected, normalized.InterfaceStyle);
    }

    [Fact]
    public void Normalize_MigratesLegacyHotkeyAndLocalLlamaSettings()
    {
        var settings = new AppSettings
        {
            SchemaVersion = 1,
            Hotkey = "Ctrl+Shift+X",
            CustomTranslationEndpoint = "http://127.0.0.1:8080/v1",
            CustomTranslationModel = "gemma4"
        };

        var normalized = settings.Normalize();

        Assert.Equal("Ctrl+Alt+D", normalized.Hotkey);
        Assert.Equal("http://127.0.0.1:8080/v1/chat/completions", normalized.CustomTranslationEndpoint);
        Assert.Equal("gemma-4-e4b-it", normalized.CustomTranslationModel);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080", "http://127.0.0.1:8080/v1/chat/completions")]
    [InlineData("http://127.0.0.1:8080/v1", "http://127.0.0.1:8080/v1/chat/completions")]
    [InlineData("http://127.0.0.1:11434", "http://127.0.0.1:11434/v1/chat/completions")]
    [InlineData("http://127.0.0.1:1234/v1", "http://127.0.0.1:1234/v1/chat/completions")]
    [InlineData("https://example.com/openai/v1/chat/completions", "https://example.com/openai/v1/chat/completions")]
    public void NormalizeChatCompletionsEndpoint_RepairsBaseUrls(string value, string expected)
    {
        Assert.Equal(expected, AppSettings.NormalizeChatCompletionsEndpoint(value));
    }

    [Fact]
    public void LocalLlmPresets_ProvideDistinctCompatibleEndpoints()
    {
        Assert.Contains(LocalLlmPresets.All, preset => preset.Id == "llama-cpp");
        Assert.Contains(LocalLlmPresets.All, preset => preset.Id == "ollama");
        Assert.Contains(LocalLlmPresets.All, preset => preset.Id == "lm-studio");
        Assert.Equal(
            LocalLlmPresets.All.Count,
            LocalLlmPresets.All.Select(preset => preset.ChatCompletionsEndpoint).Distinct().Count());
    }
}

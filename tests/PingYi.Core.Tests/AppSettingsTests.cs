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

    [Theory]
    [InlineData("auto", "auto")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("en-US", "en-US")]
    [InlineData("fr-FR", "auto")]
    [InlineData("", "auto")]
    public void Normalize_PreservesOnlySupportedUiLanguages(string value, string expected)
    {
        var normalized = new AppSettings { UiLanguage = value }.Normalize();

        Assert.Equal(expected, normalized.UiLanguage);
    }

    [Fact]
    public void LanguageCatalog_ProvidesEnglishNamesForEveryTargetLanguage()
    {
        Assert.All(LanguageCatalog.All, language => Assert.False(string.IsNullOrWhiteSpace(language.EnglishDisplayName)));
        Assert.Equal(LanguageCatalog.All.Count, LanguageCatalog.All.Select(language => language.EnglishDisplayName).Distinct().Count());
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
    [InlineData(6, true, false)]
    [InlineData(7, true, true)]
    [InlineData(7, false, false)]
    public void Normalize_RequiresExplicitUpdateCheckOptInFromSchemaSeven(
        int schemaVersion,
        bool configured,
        bool expected)
    {
        var normalized = new AppSettings
        {
            SchemaVersion = schemaVersion,
            CheckForUpdates = configured
        }.Normalize();

        Assert.Equal(expected, normalized.CheckForUpdates);
        Assert.Equal(AppSettings.CurrentSchemaVersion, normalized.SchemaVersion);
    }

    [Fact]
    public void NewSettings_DoNotCheckForUpdatesByDefault()
    {
        Assert.False(new AppSettings().CheckForUpdates);
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

    [Theory]
    [InlineData("http://127.0.0.1:8080/v1", true)]
    [InlineData("http://localhost:11434/v1", true)]
    [InlineData("https://api.example.com/v1", true)]
    [InlineData("http://api.example.com/v1", false)]
    [InlineData("http://192.168.1.20:8080/v1", false)]
    public void ChatEndpointTransport_AllowsHttpOnlyForLoopback(string value, bool expected)
    {
        Assert.True(AppSettings.TryParseChatCompletionsEndpoint(value, out var endpoint));
        Assert.Equal(expected, AppSettings.IsChatCompletionsTransportAllowed(endpoint));
    }

    [Theory]
    [InlineData("local-paddle")]
    [InlineData("local-vlm-corrected")]
    public void ApplyLocalMode_ReplacesRemoteEndpointAndModelAtomically(string ocrProviderId)
    {
        var remoteSettings = new AppSettings
        {
            OcrProviderId = "baidu-ocr",
            TranslationProviderId = "custom-chat",
            CustomTranslationEndpoint = "https://api.example.com/v1/chat/completions",
            CustomTranslationModel = "remote-model",
            ManagedRuntimeEnabled = true
        };

        var localSettings = LocalLlmPresets.ApplyLocalMode(remoteSettings, ocrProviderId);

        Assert.Equal(ocrProviderId, localSettings.OcrProviderId);
        Assert.Equal("custom-chat", localSettings.TranslationProviderId);
        Assert.Equal(AppSettings.DefaultCustomTranslationEndpoint, localSettings.CustomTranslationEndpoint);
        Assert.Equal(AppSettings.DefaultCustomTranslationModel, localSettings.CustomTranslationModel);
        Assert.False(localSettings.ManagedRuntimeEnabled);
        Assert.True(AppSettings.TryParseChatCompletionsEndpoint(localSettings.CustomTranslationEndpoint, out var endpoint));
        Assert.True(endpoint.IsLoopback);
    }

    [Fact]
    public void ApplyLocalMode_PreservesExistingLoopbackServiceAndManagedRuntime()
    {
        var settings = new AppSettings
        {
            CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint,
            CustomTranslationModel = "managed-model",
            ManagedRuntimeEnabled = true
        };

        var localSettings = LocalLlmPresets.ApplyLocalMode(settings, "local-vlm-corrected");

        Assert.Equal(AppSettings.ManagedModelEndpoint, localSettings.CustomTranslationEndpoint);
        Assert.Equal("managed-model", localSettings.CustomTranslationModel);
        Assert.True(localSettings.ManagedRuntimeEnabled);
    }
}

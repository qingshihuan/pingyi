using PingYi.Core;
using PingYi.Infrastructure;

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
            CustomTranslationModel = "  model-name  "
        };

        var normalized = settings.Normalize();

        Assert.Equal(AppSettings.CurrentSchemaVersion, normalized.SchemaVersion);
        Assert.Equal(AppSettings.DefaultHotkey, normalized.Hotkey);
        Assert.Equal("local-paddle", normalized.OcrProviderId);
        Assert.Equal("local-argos", normalized.TranslationProviderId);
        Assert.Equal("auto", normalized.SourceLanguage);
        Assert.Equal("auto-opposite", normalized.TargetLanguage);
        Assert.Equal("model-name", normalized.CustomTranslationModel);
        Assert.True(Uri.TryCreate(normalized.CustomTranslationEndpoint, UriKind.Absolute, out _));
    }

    [Fact]
    public void Normalize_MigratesExistingOfficialOpenAiEndpointToResponsesProtocol()
    {
        var settings = new AppSettings
        {
            SchemaVersion = 13,
            CustomTranslationEndpoint = "https://api.openai.com/v1/chat/completions",
            CustomTranslationApiProtocol = ModelApiProtocols.ChatCompletions
        };

        var normalized = settings.Normalize();

        Assert.Equal(ModelApiProtocols.Responses, normalized.CustomTranslationApiProtocol);
        Assert.Equal("https://api.openai.com/v1/chat/completions", normalized.CustomTranslationEndpoint);
    }

    [Fact]
    public void Normalize_PreservesExplicitChatProtocolForCurrentSchema()
    {
        var settings = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            CustomTranslationEndpoint = "https://api.openai.com/v1/chat/completions",
            CustomTranslationApiProtocol = ModelApiProtocols.ChatCompletions
        };

        Assert.Equal(ModelApiProtocols.ChatCompletions, settings.Normalize().CustomTranslationApiProtocol);
    }

    [Fact]
    public void ThirdPartyPresets_DefaultOnlyOfficialOpenAiToResponses()
    {
        Assert.Equal(ModelApiProtocols.Responses,
            ThirdPartyApiPresets.All.Single(preset => preset.Id == "openai").DefaultProtocol);
        Assert.All(ThirdPartyApiPresets.All.Where(preset => preset.Id != "openai"),
            preset => Assert.Equal(ModelApiProtocols.ChatCompletions, preset.DefaultProtocol));
    }

    [Fact]
    public void Normalize_MigratesLegacyEndpointIntoIndependentServiceAndTaskRoutes()
    {
        var normalized = new AppSettings
        {
            SchemaVersion = 14,
            OcrProviderId = "local-vlm-ocr",
            TranslationProviderId = "custom-chat",
            CustomTranslationEndpoint = "https://api.deepseek.com/chat/completions",
            CustomTranslationModel = "deepseek-chat",
            CustomTranslationApiProtocol = ModelApiProtocols.ChatCompletions,
            ManagedRuntimeBackend = "vulkan",
            ManagedRuntimeDevice = "auto",
            ManagedRuntimeEnabled = false
        }.Normalize();

        var profile = Assert.Single(normalized.Configuration.Services);
        Assert.Equal("legacy-custom", profile.Id);
        Assert.Equal(ModelServiceKinds.Remote, profile.Kind);
        Assert.Equal("https://api.deepseek.com/chat/completions", profile.Endpoint);
        Assert.Equal("deepseek-chat", profile.DefaultModelId);
        Assert.Equal(SecretKeys.CustomTranslationApiKey, profile.CredentialRef);
        Assert.Equal("legacy-custom", normalized.Configuration.Tasks.Ocr.ServiceId);
        Assert.Equal("legacy-custom", normalized.Configuration.Tasks.Translation.ServiceId);
        Assert.Equal("builtin:qr", normalized.Configuration.Tasks.QrCode.ServiceId);
        Assert.Equal("vulkan", normalized.Configuration.Runtime.Backend);
        Assert.False(normalized.Configuration.Runtime.Enabled);
        Assert.Equal(ConfigurationModes.Hybrid, normalized.Configuration.Mode);
    }

    [Fact]
    public void Configuration_PreservesMultipleServicesIndependentCredentialsAndTaskAssignments()
    {
        var configuration = new AppConfiguration
        {
            Services =
            [
                new ModelServiceProfile
                {
                    Id = "local-vision",
                    Name = "Local Vision",
                    Kind = ModelServiceKinds.Local,
                    Endpoint = "http://127.0.0.1:8080/v1/chat/completions",
                    CredentialRef = ServiceCredentialRefs.ForService("local-vision"),
                    DefaultModelId = "qwen-vl",
                    Capabilities = ["text-generation", "image-input"]
                },
                new ModelServiceProfile
                {
                    Id = "openai-cloud",
                    Name = "OpenAI",
                    Kind = ModelServiceKinds.Remote,
                    Protocol = ModelApiProtocols.Responses,
                    Endpoint = "https://api.openai.com/v1/chat/completions",
                    CredentialRef = ServiceCredentialRefs.ForService("openai-cloud"),
                    DefaultModelId = "gpt-model",
                    Capabilities = ["text-generation", "image-input"]
                }
            ],
            Tasks = new TaskRoutingConfiguration
            {
                Ocr = new TaskModelRoute { ServiceId = "local-vision", ModelId = "qwen-vl", RequireImageInput = true },
                Translation = new TaskModelRoute { ServiceId = "openai-cloud", ModelId = "gpt-model" },
                ImageDescription = new TaskModelRoute { ServiceId = "openai-cloud", ModelId = "gpt-model", RequireImageInput = true },
                PromptReconstruction = new TaskModelRoute { ServiceId = "local-vision", ModelId = "qwen-vl", RequireImageInput = true },
                QrCode = new TaskModelRoute { ServiceId = "builtin:qr", ModelId = "zxing" }
            }
        };

        var normalized = new AppSettings { Configuration = configuration }.Normalize();

        Assert.Equal(2, normalized.Configuration.Services.Count);
        Assert.NotEqual(
            normalized.Configuration.Services[0].CredentialRef,
            normalized.Configuration.Services[1].CredentialRef);
        Assert.Equal("local-vision", normalized.Configuration.Tasks.Ocr.ServiceId);
        Assert.Equal("openai-cloud", normalized.Configuration.Tasks.Translation.ServiceId);
        Assert.Equal(ModelApiProtocols.Responses,
            normalized.Configuration.Services.Single(service => service.Id == "openai-cloud").Protocol);
        Assert.Equal("builtin:qr", normalized.Configuration.Tasks.QrCode.ServiceId);
    }

    [Fact]
    public void SettingsJsonContainsCredentialReferencesButNeverSecretValues()
    {
        const string secret = "sk-test-never-serialize-this";
        var settings = new AppSettings
        {
            Configuration = new AppConfiguration
            {
                Services =
                [
                    new ModelServiceProfile
                    {
                        Id = "openai-cloud",
                        Name = "OpenAI",
                        Endpoint = "https://api.openai.com/v1/chat/completions",
                        CredentialRef = ServiceCredentialRefs.ForService("openai-cloud")
                    }
                ]
            }
        }.Normalize();

        var json = System.Text.Json.JsonSerializer.Serialize(settings);

        Assert.Contains(ServiceCredentialRefs.ForService("openai-cloud"), json);
        Assert.DoesNotContain(secret, json);
        Assert.DoesNotContain("sk-", json);
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

    [Fact]
    public async Task LegacyInterfaceStyle_IsIgnoredWithoutResettingPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(file, """
                {"schemaVersion":7,"interfaceStyle":"classic","uiLanguage":"en-US",
                 "hotkey":"Ctrl+Alt+G","translationProviderId":"custom-chat",
                 "customTranslationEndpoint":"https://example.com/v1/chat/completions",
                 "customTranslationModel":"kept-model","checkForUpdates":true}
                """);
            var store = new JsonSettingsStore(file);
            var settings = await store.LoadAsync();
            Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
            Assert.Equal("en-US", settings.UiLanguage);
            Assert.Equal("Ctrl+Alt+G", settings.Hotkey);
            Assert.Equal("kept-model", settings.CustomTranslationModel);
            Assert.Equal("custom-chat", settings.TranslationProviderId);
            Assert.True(settings.CheckForUpdates);
            await store.SaveAsync(settings);
            Assert.DoesNotContain("interfaceStyle", await File.ReadAllTextAsync(file));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FormerDefaultHotkey_PersistsThePlatformMigrationAndOtherPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-hotkey-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(file, """
                {"schemaVersion":8,"uiLanguage":"en-US","hotkey":"Ctrl+Alt+D",
                 "customTranslationModel":"kept-model","startMinimized":true}
                """);
            var store = new JsonSettingsStore(file);
            var migrated = await store.LoadAsync();
            Assert.Equal(AppSettings.DefaultHotkey, migrated.Hotkey);
            Assert.Equal(AppSettings.CurrentSchemaVersion, migrated.SchemaVersion);
            Assert.True(migrated.StartMinimized);
            Assert.Equal("kept-model", migrated.CustomTranslationModel);
            await store.SaveAsync(migrated);
            Assert.Equal(migrated, await store.LoadAsync());
        }
        finally { Directory.Delete(directory, true); }
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

        Assert.Equal(AppSettings.DefaultHotkey, normalized.Hotkey);
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

    [Fact]
    public void ThirdPartyApiPresets_UseUniqueSecureOpenAiCompatibleEndpoints()
    {
        Assert.Contains(ThirdPartyApiPresets.All, preset => preset.Id == "openai");
        Assert.Contains(ThirdPartyApiPresets.All, preset => preset.Id == "deepseek");
        Assert.Contains(ThirdPartyApiPresets.All, preset => preset.Id == "siliconflow");
        Assert.Contains(ThirdPartyApiPresets.All, preset => preset.Id == "openrouter");
        Assert.Contains(ThirdPartyApiPresets.All, preset => preset.Id == "dashscope");
        Assert.Contains(ThirdPartyApiPresets.All, preset => preset.Id == "zhipu");
        Assert.Equal(ThirdPartyApiPresets.All.Count,
            ThirdPartyApiPresets.All.Select(preset => preset.ChatCompletionsEndpoint).Distinct().Count());
        Assert.All(ThirdPartyApiPresets.All, preset =>
        {
            Assert.True(AppSettings.TryParseChatCompletionsEndpoint(preset.ChatCompletionsEndpoint, out var endpoint));
            Assert.Equal(Uri.UriSchemeHttps, endpoint.Scheme);
            Assert.True(AppSettings.IsChatCompletionsTransportAllowed(endpoint));
        });
    }

    [Fact]
    public void ThirdPartyApiPresets_MatchNormalizedEndpoint()
    {
        var preset = ThirdPartyApiPresets.All.Single(item => item.Id == "deepseek");
        Assert.Equal(preset, ThirdPartyApiPresets.MatchEndpoint("https://api.deepseek.com/chat/completions/"));
    }

    [Fact]
    public void ChatEndpoint_RejectsCredentialsEmbeddedInUrl()
    {
        Assert.False(AppSettings.TryParseChatCompletionsEndpoint(
            "https://user:secret@example.com/v1/chat/completions", out _));
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

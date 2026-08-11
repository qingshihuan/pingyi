namespace PingYi.Core;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 6;
    public const string DefaultHotkey = "Ctrl+Alt+D";
    public const string DefaultCustomTranslationEndpoint = "http://127.0.0.1:8080/v1/chat/completions";
    public const string DefaultCustomTranslationModel = "gemma-4-e4b-it";
    public const string ManagedModelEndpoint = "http://127.0.0.1:18080/v1/chat/completions";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Hotkey { get; init; } = DefaultHotkey;
    public string OcrProviderId { get; init; } = "local-paddle";
    public string TranslationProviderId { get; init; } = "local-argos";
    public string SourceLanguage { get; init; } = "auto";
    public string TargetLanguage { get; init; } = "auto-opposite";
    public string CustomTranslationEndpoint { get; init; } = DefaultCustomTranslationEndpoint;
    public string CustomTranslationModel { get; init; } = DefaultCustomTranslationModel;
    public string ManagedModelPackageId { get; init; } = string.Empty;
    public string ManagedRuntimeBackend { get; init; } = ManagedRuntimeBackends.Auto.Id;
    public bool ManagedRuntimeEnabled { get; init; }
    public bool StartMinimized { get; init; }
    public bool CheckForUpdates { get; init; } = true;
    public string InterfaceStyle { get; init; } = "modern";
    public string UiLanguage { get; init; } = "auto";

    public AppSettings Normalize()
    {
        var hotkey = string.IsNullOrWhiteSpace(Hotkey) ? DefaultHotkey : Hotkey.Trim();
        if (SchemaVersion < 2 && string.Equals(hotkey, "Ctrl+Shift+X", StringComparison.OrdinalIgnoreCase))
        {
            hotkey = DefaultHotkey;
        }

        var endpoint = NormalizeChatCompletionsEndpoint(CustomTranslationEndpoint);
        var model = CustomTranslationModel.Trim();
        if (SchemaVersion < 2 &&
            string.Equals(model, "gemma4", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(endpoint, UriKind.Absolute, out var migratedEndpoint) &&
            migratedEndpoint.IsLoopback && migratedEndpoint.Port == 8080)
        {
            model = DefaultCustomTranslationModel;
        }

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            Hotkey = hotkey,
            OcrProviderId = string.IsNullOrWhiteSpace(OcrProviderId) ? "local-paddle" : OcrProviderId,
            TranslationProviderId = string.IsNullOrWhiteSpace(TranslationProviderId) ? "local-argos" : TranslationProviderId,
            SourceLanguage = LanguageCatalog.NormalizeSource(SourceLanguage),
            TargetLanguage = LanguageCatalog.NormalizeTarget(TargetLanguage),
            CustomTranslationEndpoint = endpoint,
            CustomTranslationModel = model,
            ManagedModelPackageId = ManagedMultimodalModels.TryGet(ManagedModelPackageId, out _)
                ? ManagedModelPackageId.Trim()
                : string.Empty,
            ManagedRuntimeBackend = ManagedRuntimeBackends.Normalize(ManagedRuntimeBackend),
            ManagedRuntimeEnabled = ManagedRuntimeEnabled &&
                                    ManagedMultimodalModels.TryGet(ManagedModelPackageId, out _) &&
                                    string.Equals(endpoint, ManagedModelEndpoint, StringComparison.OrdinalIgnoreCase),
            InterfaceStyle = InterfaceStyle is "classic" ? "classic" : "modern",
            UiLanguage = UiLanguage is "zh-CN" or "en-US" ? UiLanguage : "auto"
        };
    }

    public static string NormalizeChatCompletionsEndpoint(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            return DefaultCustomTranslationEndpoint;
        }

        var path = endpoint.AbsolutePath.TrimEnd('/');
        if (string.IsNullOrEmpty(path))
        {
            path = "/v1/chat/completions";
        }
        else if (string.Equals(path, "/v1", StringComparison.OrdinalIgnoreCase))
        {
            path = "/v1/chat/completions";
        }

        var builder = new UriBuilder(endpoint)
        {
            Path = path,
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }
}

public static class SecretKeys
{
    public const string BaiduOcrApiKey = "baidu-ocr-api-key";
    public const string BaiduOcrSecretKey = "baidu-ocr-secret-key";
    public const string BaiduTranslateAppId = "baidu-translate-app-id";
    public const string BaiduTranslateSecret = "baidu-translate-secret";
    public const string GoogleCloudApiKey = "google-cloud-api-key";
    public const string CustomTranslationApiKey = "custom-translation-api-key";
}

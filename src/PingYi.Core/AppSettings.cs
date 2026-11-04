using System.Text.Json.Serialization;

namespace PingYi.Core;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 16;
    public const string WindowsDefaultHotkey = "Ctrl+Alt+D";
    public const string LinuxDefaultHotkey = "Ctrl+Shift+D";
    public const string PreviousLinuxDefaultHotkey = "Ctrl+Alt+Shift+D";
    public static string DefaultHotkey => OperatingSystem.IsLinux() ? LinuxDefaultHotkey : WindowsDefaultHotkey;
    public const string DefaultCustomTranslationEndpoint = "http://127.0.0.1:8080/v1/chat/completions";
    public const string DefaultCustomTranslationModel = "gemma-4-e4b-it";
    public const string ManagedModelEndpoint = "http://127.0.0.1:18080/v1/chat/completions";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public ModelConfiguration? ModelConfiguration { get; init; } = new();
    public string Hotkey { get; init; } = DefaultHotkey;
    public bool InitialSetupCompleted { get; init; }
    public bool AutomaticCaptureEnabled { get; init; } = true;
    public string RemoteCaptureApprovals { get; init; } = "";
    public string SourceLanguage { get; init; } = "auto";
    public string TargetLanguage { get; init; } = "auto-opposite";
    [JsonIgnore] public string CustomTranslationEndpoint { get; init; } = DefaultCustomTranslationEndpoint;
    [JsonIgnore] public string CustomTranslationModel { get; init; } = DefaultCustomTranslationModel;
    [JsonIgnore] public string CustomTranslationApiProtocol { get; init; } = ModelApiProtocols.ChatCompletions;
    public string ManagedModelPackageId { get; init; } = string.Empty;
    public string ManagedRuntimeBackend { get; init; } = ManagedRuntimeBackends.Default.Id;
    public string ManagedRuntimeDevice { get; init; } = RuntimeDeviceChoice.Automatic;
    public bool RuntimeAllowMirrors { get; init; }
    public string RuntimeMirrorPrefixes { get; init; } = string.Empty;
    public bool ManagedRuntimeEnabled { get; init; }
    public bool StartMinimized { get; init; }
    public bool CheckForUpdates { get; init; }
    public string UiLanguage { get; init; } = "auto";

    public AppSettings Normalize() => NormalizeForPlatform(OperatingSystem.IsLinux());

    public AppSettings NormalizeForPlatform(bool linux)
    {
        var defaultHotkey = linux ? LinuxDefaultHotkey : WindowsDefaultHotkey;
        var hotkey = string.IsNullOrWhiteSpace(Hotkey) ? defaultHotkey : Hotkey.Trim();
        if (SchemaVersion < 2 && string.Equals(hotkey, "Ctrl+Shift+X", StringComparison.OrdinalIgnoreCase))
            hotkey = defaultHotkey;
        var compactHotkey = hotkey.Replace(" ", "", StringComparison.Ordinal);
        if (linux && ((SchemaVersion < 9 &&
                string.Equals(compactHotkey, WindowsDefaultHotkey, StringComparison.OrdinalIgnoreCase)) ||
            (SchemaVersion < 10 &&
                string.Equals(compactHotkey, PreviousLinuxDefaultHotkey, StringComparison.OrdinalIgnoreCase))))
            hotkey = LinuxDefaultHotkey;

        var endpoint = NormalizeChatCompletionsEndpoint(CustomTranslationEndpoint);
        var model = CustomTranslationModel?.Trim() ?? DefaultCustomTranslationModel;
        if (SchemaVersion < 2 && string.Equals(model, "gemma4", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(endpoint, UriKind.Absolute, out var migratedEndpoint) &&
            migratedEndpoint.IsLoopback && migratedEndpoint.Port == 8080)
            model = DefaultCustomTranslationModel;

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            Hotkey = hotkey,
            InitialSetupCompleted = InitialSetupCompleted || SchemaVersion < 11,
            RemoteCaptureApprovals = RemoteCaptureConsent.NormalizeApprovals(RemoteCaptureApprovals),
            SourceLanguage = LanguageCatalog.NormalizeSource(SourceLanguage),
            TargetLanguage = LanguageCatalog.NormalizeTarget(TargetLanguage),
            CustomTranslationEndpoint = endpoint,
            CustomTranslationModel = model,
            CustomTranslationApiProtocol = ModelApiProtocols.Normalize(
                SchemaVersion < 14 && ModelApiProtocols.IsOfficialOpenAiEndpoint(endpoint)
                    ? ModelApiProtocols.Responses
                    : CustomTranslationApiProtocol),
            ManagedModelPackageId = ManagedMultimodalModels.TryGet(ManagedModelPackageId, out _)
                ? ManagedModelPackageId.Trim() : string.Empty,
            ManagedRuntimeBackend = ManagedRuntimeBackends.Normalize(ManagedRuntimeBackend),
            ManagedRuntimeDevice = RuntimeDeviceChoice.Normalize(ManagedRuntimeDevice),
            RuntimeMirrorPrefixes = RuntimeMirrorPrefixes?.Trim() ?? string.Empty,
            ManagedRuntimeEnabled = ManagedRuntimeEnabled && ManagedMultimodalModels.TryGet(ManagedModelPackageId, out _),
            CheckForUpdates = SchemaVersion >= 7 && CheckForUpdates,
            UiLanguage = UiLanguage is "zh-CN" or "en-US" ? UiLanguage : "auto"
        };
    }

    public static string NormalizeChatCompletionsEndpoint(string? value)
    {
        if (!TryParseChatCompletionsEndpoint(value, out var endpoint)) return DefaultCustomTranslationEndpoint;
        return endpoint.AbsoluteUri.TrimEnd('/');
    }

    public static bool TryParseChatCompletionsEndpoint(string? value, out Uri endpoint)
    {
        endpoint = null!;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsedEndpoint) ||
            parsedEndpoint.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(parsedEndpoint.UserInfo)) return false;
        var path = parsedEndpoint.AbsolutePath.TrimEnd('/');
        if (string.IsNullOrEmpty(path) || string.Equals(path, "/v1", StringComparison.OrdinalIgnoreCase))
            path = "/v1/chat/completions";
        endpoint = new UriBuilder(parsedEndpoint) { Path = path, Query = string.Empty, Fragment = string.Empty }.Uri;
        return true;
    }

    public static bool IsChatCompletionsTransportAllowed(Uri endpoint) =>
        endpoint.IsLoopback || string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}

public static class SecretKeys
{
    public const string CustomTranslationApiKey = "custom-translation-api-key";
}

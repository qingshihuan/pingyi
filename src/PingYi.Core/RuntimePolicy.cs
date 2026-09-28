namespace PingYi.Core;

/// <summary>Configuration is not a request to preload a model.</summary>
public static class RuntimePolicy
{
    public static bool HasConfiguredManagedRuntime(AppSettings settings) =>
        settings.ManagedRuntimeEnabled && ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out _) &&
        string.Equals(AppSettings.NormalizeChatCompletionsEndpoint(settings.CustomTranslationEndpoint),
            AppSettings.ManagedModelEndpoint, StringComparison.OrdinalIgnoreCase);
    public static bool UsesManagedRuntime(AppSettings settings) => HasConfiguredManagedRuntime(settings) &&
        (settings.OcrProviderId == "local-vlm-ocr" || settings.TranslationProviderId == "custom-chat");
    public static int OcrThreadCount(int logicalProcessors) => Math.Clamp(logicalProcessors / 2, 1, 4);
}

public sealed class PassiveRefreshPolicy(TimeSpan maxAge, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private AppSettings? _settings;
    private long _lastRefresh;
    public bool ShouldRefresh(AppSettings settings) => _settings != settings || _clock.GetElapsedTime(_lastRefresh) >= maxAge;
    public void RecordRefresh(AppSettings settings) { _settings = settings; _lastRefresh = _clock.GetTimestamp(); }
}

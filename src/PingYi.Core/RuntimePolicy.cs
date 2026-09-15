namespace PingYi.Core;

/// <summary>Pure runtime policy: configuration is not a request to preload a model.</summary>
public static class RuntimePolicy
{
    public static bool UsesManagedRuntime(AppSettings settings) =>
        settings.ManagedRuntimeEnabled &&
        ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out _) &&
        string.Equals(AppSettings.NormalizeChatCompletionsEndpoint(settings.CustomTranslationEndpoint),
            AppSettings.ManagedModelEndpoint, StringComparison.OrdinalIgnoreCase) &&
        (settings.OcrProviderId is "local-vlm-ocr" or "local-vlm-corrected" ||
         settings.TranslationProviderId == "custom-chat");

    // OCR is an interactive desktop task, not an all-core batch workload.
    public static int OcrThreadCount(int logicalProcessors) =>
        Math.Clamp(logicalProcessors / 2, 1, 4);
}

/// <summary>Only passive UI refreshes are throttled. Never cache inference or credentials.</summary>
public sealed class PassiveRefreshPolicy(TimeSpan maxAge, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private AppSettings? _settings;
    private long _lastRefresh;

    public bool ShouldRefresh(AppSettings settings) =>
        _settings != settings || _clock.GetElapsedTime(_lastRefresh) >= maxAge;

    public void RecordRefresh(AppSettings settings)
    {
        _settings = settings;
        _lastRefresh = _clock.GetTimestamp();
    }
}

namespace PingYi.Core;

/// <summary>Configuration is not a request to preload a model.</summary>
public static class RuntimePolicy
{
    public static bool HasConfiguredManagedRuntime(AppSettings settings) =>
        settings.ManagedRuntimeEnabled && ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out _) &&
        (string.Equals(AppSettings.NormalizeChatCompletionsEndpoint(settings.CustomTranslationEndpoint),
            AppSettings.ManagedModelEndpoint, StringComparison.OrdinalIgnoreCase) ||
         settings.ModelConfiguration?.Tasks is { } routes && ModelTasks.All.Any(t => routes.Get(t).ServiceId == ModelConfiguration.Managed));
    public static bool UsesManagedRuntime(AppSettings settings) => HasConfiguredManagedRuntime(settings) &&
        new[] { ModelTasks.Ocr, ModelTasks.Translation }.Any(task =>
            settings.ModelConfiguration?.Tasks.Get(task).ServiceId == ModelConfiguration.Managed ||
            string.IsNullOrEmpty(settings.ModelConfiguration?.Tasks.Get(task).ServiceId) &&
            (task == ModelTasks.Ocr ? settings.OcrProviderId == "local-vlm-ocr" : settings.TranslationProviderId == "custom-chat") &&
            settings.CustomTranslationEndpoint == AppSettings.ManagedModelEndpoint);
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

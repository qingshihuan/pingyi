namespace PingYi.Core;

/// <summary>Configuration is not a request to preload a model.</summary>
public static class RuntimePolicy
{
    public static bool HasConfiguredManagedRuntime(AppSettings settings) =>
        settings.ManagedRuntimeEnabled && ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out _) &&
        (string.Equals(AppSettings.NormalizeChatCompletionsEndpoint(settings.CustomTranslationEndpoint),
            AppSettings.ManagedModelEndpoint, StringComparison.OrdinalIgnoreCase) ||
         settings.ModelConfiguration?.Tasks is { } routes && ModelTasks.All.Any(t => routes.Get(t).ServiceId == ModelConfiguration.Managed));
    public static bool UsesManagedRuntime(AppSettings settings) =>
        UsesManagedRuntimeForTask(settings, ModelTasks.Ocr) || UsesManagedRuntimeForTask(settings, ModelTasks.Translation);

    public static bool UsesAnyManagedRuntime(AppSettings settings) =>
        ModelTasks.All.Any(task => UsesManagedRuntimeForTask(settings, task));

    /// <summary>A task that does not use the owned runtime must not load it for another task.</summary>
    public static bool UsesManagedRuntimeForTask(AppSettings settings, string task)
    {
        if (!ModelTasks.All.Contains(task)) throw new ArgumentOutOfRangeException(nameof(task));
        if (!HasConfiguredManagedRuntime(settings)) return false;
        var id = settings.ModelConfiguration?.Tasks.Get(task).ServiceId ?? "";
        if (id.Length > 0) return id == ModelConfiguration.Managed;
        if (!string.Equals(AppSettings.NormalizeChatCompletionsEndpoint(settings.CustomTranslationEndpoint),
                AppSettings.ManagedModelEndpoint, StringComparison.OrdinalIgnoreCase)) return false;
        return task switch
        {
            ModelTasks.Ocr => settings.OcrProviderId == "local-vlm-ocr",
            ModelTasks.Translation => settings.TranslationProviderId == "custom-chat",
            _ => true
        };
    }
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

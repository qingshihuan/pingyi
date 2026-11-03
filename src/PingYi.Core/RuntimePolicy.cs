namespace PingYi.Core;

/// <summary>Configuration is not a request to preload a model.</summary>
public static class RuntimePolicy
{
    private static bool IsManagedRoute(AppSettings settings, TaskModelRoute route)
    {
        if (!settings.ManagedRuntimeEnabled || !ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out _))
            return false;
        var service = settings.Configuration.Services.FirstOrDefault(item => item.Id == route.ServiceId);
        var endpoint = service?.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint) && route.ServiceId == "legacy-custom")
            endpoint = settings.CustomTranslationEndpoint;
        return string.Equals(AppSettings.NormalizeChatCompletionsEndpoint(endpoint),
            AppSettings.ManagedModelEndpoint, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasConfiguredManagedRuntime(AppSettings settings) =>
        IsManagedRoute(settings, settings.Configuration.Tasks.Ocr) ||
        IsManagedRoute(settings, settings.Configuration.Tasks.Translation) ||
        IsManagedRoute(settings, settings.Configuration.Tasks.ImageDescription) ||
        IsManagedRoute(settings, settings.Configuration.Tasks.PromptReconstruction);

    public static bool HasConfiguredManagedRuntime(AppSettings settings, string taskId) =>
        IsManagedRoute(settings, settings.Configuration.Tasks.GetRoute(taskId));

    public static bool UsesManagedRuntime(AppSettings settings) =>
        UsesManagedRuntime(settings, ModelTaskIds.Ocr) ||
        UsesManagedRuntime(settings, ModelTaskIds.Translation);

    public static bool UsesManagedRuntime(AppSettings settings, string taskId) =>
        IsManagedRoute(settings, settings.Configuration.Tasks.GetRoute(taskId));
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

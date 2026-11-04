namespace PingYi.Core;

/// <summary>Configuration is not a request to preload a model.</summary>
public static class RuntimePolicy
{
    public static bool HasConfiguredManagedRuntime(AppSettings settings) =>
        settings.ManagedRuntimeEnabled && ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out _);
    public static bool UsesManagedRuntime(AppSettings settings) => HasConfiguredManagedRuntime(settings) &&
        settings.ModelConfiguration?.CurrentModel?.ServiceId == ModelConfiguration.Managed;
    public static bool UsesAnyManagedRuntime(AppSettings settings) => UsesManagedRuntime(settings);
    public static bool UsesManagedRuntimeForTask(AppSettings settings, string task)
    {
        if (!ModelTasks.All.Contains(task)) throw new ArgumentOutOfRangeException(nameof(task));
        return UsesManagedRuntime(settings);
    }

}

public sealed class PassiveRefreshPolicy(TimeSpan maxAge, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private AppSettings? _settings;
    private long _lastRefresh;
    public bool ShouldRefresh(AppSettings settings) => _settings != settings || _clock.GetElapsedTime(_lastRefresh) >= maxAge;
    public void RecordRefresh(AppSettings settings) { _settings = settings; _lastRefresh = _clock.GetTimestamp(); }
}

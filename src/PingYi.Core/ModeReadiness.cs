namespace PingYi.Core;

public enum ModeReadinessState { Unknown, Available, OnDemand, Unconfigured, NeedsAttention, Unverified, Verified }
public sealed record ModeReadiness(ModeReadinessState State, string Reason)
{
    public bool IsReady => State is ModeReadinessState.Available or ModeReadinessState.OnDemand;
    public static ModeReadiness Unknown { get; } = new(ModeReadinessState.Unknown, "not-checked");
}
public sealed record BasicModeEvidence(bool Configured, bool Managed, bool ModelFilesPresent,
    bool RuntimeInstalled, bool Running, bool? LocalServiceReachable);
public static class ModeReadinessPolicy
{
    public static ModeReadiness Basic(BasicModeEvidence value)
    {
        if (!value.Configured) return new(ModeReadinessState.Unconfigured, "basic-unconfigured");
        if (!value.Managed) return value.LocalServiceReachable switch
        {
            true => new(ModeReadinessState.Unverified, "local-configured"),
            false => new(ModeReadinessState.NeedsAttention, "local-unreachable"),
            _ => new(ModeReadinessState.Unverified, "local-configured")
        };
        if (!value.ModelFilesPresent) return new(ModeReadinessState.NeedsAttention, "model-missing");
        if (!value.RuntimeInstalled) return new(ModeReadinessState.NeedsAttention, "runtime-missing");
        return value.Running ? new(ModeReadinessState.Available, "basic-running")
            : new(ModeReadinessState.OnDemand, "basic-on-demand");
    }
    public static bool HasApiConfiguration(AppSettings settings) => settings.ModelConfiguration?.AllServices.Any() == true;
    public static bool HasLocalConfiguration(AppSettings settings) => RuntimePolicy.HasConfiguredManagedRuntime(settings);
    public static ModeReadiness Api(AppSettings settings)
    {
        if (!HasApiConfiguration(settings)) return new(ModeReadinessState.Unconfigured, "api-unconfigured");
        return settings.ModelConfiguration!.AllServices.Any(s => s.Models.Any(m => m.TextVerified && m.VisionVerified))
            ? new(ModeReadinessState.Verified, "api-tested")
            : new(ModeReadinessState.Unverified, "api-unverified");
    }
    public static ModeReadiness Current(AppSettings settings, ModeReadiness local)
    {
        try
        {
            var route = ConfigurationRouting.Resolve(settings, ModelTasks.Ocr);
            return route.IsManaged ? local : new(ModeReadinessState.Verified, "current-tested");
        }
        catch (ProviderException) { return new(ModeReadinessState.Unconfigured, "current-unconfigured"); }
    }
}

namespace PingYi.Core;

public enum ModeReadinessState { Unknown, Available, OnDemand, Unconfigured, NeedsAttention, Unverified }
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
    public static bool HasRemoteEndpoint(AppSettings settings) => settings.ModelConfiguration?.Services.Length > 0;
    public static bool HasLocalConfiguration(AppSettings settings) => RuntimePolicy.HasConfiguredManagedRuntime(settings) ||
        settings.ModelConfiguration?.LocalServices.Length > 0;
}

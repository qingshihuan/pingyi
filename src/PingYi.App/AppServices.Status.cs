using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

internal sealed record RuntimeStatusSnapshot(AppSettings Settings, ModeReadiness Current,
    ModeReadiness Basic, ModeReadiness Api, PassiveModelStatus? Model, InstalledRuntime? Runtime, DateTimeOffset? CheckedAt)
{
    public static RuntimeStatusSnapshot Unknown { get; } = new(new AppSettings(), ModeReadiness.Unknown,
        ModeReadiness.Unknown, ModeReadiness.Unknown, null, null, null);
}

public sealed partial class AppServices
{
    private readonly SemaphoreSlim _statusReadGate = new(1, 1);
    private RuntimeStatusSnapshot? _lastStatus;
    internal async Task<RuntimeStatusSnapshot> ReadRuntimeStatusAsync(CancellationToken token, bool force = false)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        token = operation.Token;
        await _statusReadGate.WaitAsync(token);
        try
        {
            var settings = Settings;
            if (!force && _lastStatus is { CheckedAt: { } checkedAt } cached && cached.Settings == settings &&
                DateTimeOffset.UtcNow - checkedAt < TimeSpan.FromSeconds(5)) return cached;
            var model = ManagedModels.InspectForStatus(settings);
            InstalledRuntime? runtime = null;
            try { runtime = await ManagedModels.Runtimes.RecommendedInstalledAsync(settings.ManagedRuntimeBackend, settings.ManagedRuntimeDevice, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { }
            var local = ModeReadinessPolicy.Basic(new(ModeReadinessPolicy.HasLocalConfiguration(settings),
                RuntimePolicy.HasConfiguredManagedRuntime(settings), model.FilesPresent, runtime is not null, model.Running, null));
            var api = new ModeReadiness(ModeReadinessPolicy.HasApiConfiguration(settings) ? ModeReadinessState.Unverified : ModeReadinessState.Unconfigured,
                ModeReadinessPolicy.HasApiConfiguration(settings) ? "api-unverified" : "api-unconfigured");
            ModeReadiness current;
            try
            {
                var route = ConfigurationRouting.Resolve(settings, ModelTasks.Ocr);
                current = route.IsManaged ? local : new(ModeReadinessState.Unverified, "current-tested");
            }
            catch (ProviderException) { current = new(ModeReadinessState.Unconfigured, "current-unconfigured"); }
            var snapshot = new RuntimeStatusSnapshot(settings, current, local, api, model, runtime, DateTimeOffset.UtcNow);
            if (Settings == settings) _lastStatus = snapshot;
            return snapshot;
        }
        finally { _statusReadGate.Release(); }
    }
}

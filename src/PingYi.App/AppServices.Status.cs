using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

internal sealed record RuntimeStatusSnapshot(AppSettings Settings, ModeReadiness Current,
    ModeReadiness Basic, ModeReadiness Api, PassiveModelStatus? Model, InstalledRuntime? Runtime, DateTimeOffset? CheckedAt)
{
    public static RuntimeStatusSnapshot Unknown { get; } = new(new AppSettings(), ModeReadiness.Unknown,
        ModeReadiness.Unknown, ModeReadiness.Unknown, null, null, null);
    internal bool SameState(RuntimeStatusSnapshot other) => Settings == other.Settings && Current == other.Current &&
        Basic == other.Basic && Api == other.Api && Model == other.Model && Runtime == other.Runtime;
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
            var model = ManagedModels.InspectForStatus(settings);
            if (!force && _lastStatus is { CheckedAt: { } checkedAt } cached && cached.Settings == settings &&
                cached.Model == model && DateTimeOffset.UtcNow - checkedAt < TimeSpan.FromSeconds(5)) return cached;
            InstalledRuntime? runtime = null;
            try { runtime = await ManagedModels.Runtimes.RecommendedInstalledAsync(model.RunningBackend ?? settings.ManagedRuntimeBackend, settings.ManagedRuntimeDevice, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { }
            var local = ModeReadinessPolicy.Basic(new(ModeReadinessPolicy.HasLocalConfiguration(settings),
                RuntimePolicy.HasConfiguredManagedRuntime(settings), model.FilesPresent, runtime is not null, model.Running, null));
            var api = ModeReadinessPolicy.Api(settings);
            var current = ModeReadinessPolicy.Current(settings, local);
            var snapshot = new RuntimeStatusSnapshot(settings, current, local, api, model, runtime, DateTimeOffset.UtcNow);
            if (Settings == settings) _lastStatus = snapshot;
            return snapshot;
        }
        finally { _statusReadGate.Release(); }
    }
}

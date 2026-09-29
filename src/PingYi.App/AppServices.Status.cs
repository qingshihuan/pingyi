using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

internal sealed record RuntimeStatusSnapshot(AppSettings Settings, ModeReadiness Lightweight,
    ModeReadiness Basic, ModeReadiness Cloud, bool? PaddleReady, bool? ArgosReady,
    PassiveModelStatus? Model, InstalledRuntime? Runtime, DateTimeOffset? CheckedAt)
{
    public static RuntimeStatusSnapshot Unknown { get; } = new(new AppSettings(), ModeReadiness.Unknown,
        ModeReadiness.Unknown, ModeReadiness.Unknown, null, null, null, null, null);
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
            var paddle = ProbeAsync(ct => PaddleProvider.GetAvailabilityAsync(ct).AsTask(), token);
            var argos = ProbeAsync(ct => ArgosProvider.GetAvailabilityAsync(ct).AsTask(), token);
            var cloud = ReadCloudStateAsync(settings, SecretStore, token);
            var managed = RuntimePolicy.HasConfiguredManagedRuntime(settings);
            var localConfigured = ModeReadinessPolicy.HasLocalConfiguration(settings);
            var local = ProbeLocalServiceAsync(settings, managed,
                ct => new ChatCompatibleTranslationProvider(_inferenceClient, SecretStore, () => settings)
                    .GetAvailabilityAsync(ct).AsTask(), token);
            var model = ManagedModels.InspectForStatus(settings);
            InstalledRuntime? runtime = null;
            try
            {
                runtime = await ManagedModels.Runtimes.RecommendedInstalledAsync(settings.ManagedRuntimeBackend,
                    settings.ManagedRuntimeDevice, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { } // One broken backend must not hide lightweight/cloud state.
            await Task.WhenAll(paddle, argos, cloud, local);
            token.ThrowIfCancellationRequested();
            var snapshot = new RuntimeStatusSnapshot(settings,
                ModeReadinessPolicy.Lightweight(await paddle, await argos),
                ModeReadinessPolicy.Basic(new(localConfigured, managed, model.FilesPresent,
                    runtime is not null, model.Running, await local)),
                await cloud, await paddle, await argos, model, runtime, DateTimeOffset.UtcNow);
            if (Settings == settings) _lastStatus = snapshot;
            return snapshot;
        }
        finally { _statusReadGate.Release(); }
    }

    internal static Task<bool?> ProbeLocalServiceAsync(AppSettings settings, bool managed,
        Func<CancellationToken, Task<ProviderAvailability>> probe, CancellationToken token) =>
        !managed && ModeReadinessPolicy.HasLocalConfiguration(settings)
            ? ProbeAsync(probe, token) : Task.FromResult<bool?>(null);

    private static async Task<bool?> ProbeAsync(Func<CancellationToken, Task<ProviderAvailability>> probe, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try { return (await probe(timeout.Token)).IsAvailable; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return false; } // No dependency output or user content is retained.
    }

    internal static async Task<ModeReadiness> ReadCloudStateAsync(AppSettings settings, ISecretStore store, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            // Read only presence; never return, log or persist credential values in a status snapshot.
            async Task<bool> Has(string key) => !string.IsNullOrWhiteSpace(await store.GetAsync(key, timeout.Token));
            var google = await Has(SecretKeys.GoogleCloudApiKey);
            var ocrKey = await Has(SecretKeys.BaiduOcrApiKey);
            var ocrSecret = await Has(SecretKeys.BaiduOcrSecretKey);
            var appId = await Has(SecretKeys.BaiduTranslateAppId);
            var translationSecret = await Has(SecretKeys.BaiduTranslateSecret);
            return ModeReadinessPolicy.Cloud(new(true, google, ocrKey, ocrSecret, appId, translationSecret,
                ModeReadinessPolicy.HasRemoteEndpoint(settings)));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return ModeReadinessPolicy.Cloud(new(false, false, false, false, false, false,
                ModeReadinessPolicy.HasRemoteEndpoint(settings)));
        }
    }
}

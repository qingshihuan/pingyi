using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public sealed partial class AppServices
{
    private readonly SemaphoreSlim _initialModelGate = new(1, 1);
    public bool IsInitialSetupActive { get; internal set; }

    public async Task SaveAutomaticCapturePreferenceAsync(bool enabled, CancellationToken token = default)
    {
        await _settingsTransitionGate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            var next = Settings with { AutomaticCaptureEnabled = enabled };
            await SettingsStore.SaveAsync(next, token);
            Settings = next;
            // A task-selection preference must not start a large model.
        }
        finally { _settingsTransitionGate.Release(); }
    }

    /// <summary>Explicit user action only; success means both request types were verified.</summary>
    public Task ConfigureInitialModelAsync(ManagedMultimodalModel model, string backend,
        IProgress<ManagedModelProgress> progress, CancellationToken token) =>
        ConfigureInitialModelAsync(model, backend, Settings.ManagedRuntimeDevice, Settings.RuntimeAllowMirrors,
            Settings.RuntimeMirrorPrefixes, progress, token);

    public async Task ConfigureInitialModelAsync(ManagedMultimodalModel model, string backend, string device,
        bool allowMirrors, string mirrorPrefixes, IProgress<ManagedModelProgress> progress, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(IsShuttingDown, this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var cancellation = lifetime.Token;
        await _initialModelGate.WaitAsync(cancellation);
        var previouslyActive = IsInitialSetupActive;
        IsInitialSetupActive = true;
        var before = Settings;
        var attemptedStart = false;
        try
        {
            // Installed Vulkan/CPU do not require an online update just to configure a model.
            if (await ManagedModels.Runtimes.RecommendedInstalledAsync(backend, device, cancellation) is null)
                await ManagedModels.Runtimes.InstallAsync(backend, device, allowMirrors,
                    mirrorPrefixes.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), progress, cancellation);
            await ManagedModels.DownloadAsync(model, progress, cancellation);
            attemptedStart = true;
            await ManagedModels.EnsureStartedAsync(model, backend, progress, cancellation, device);
            var configured = before with
            {
                CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint, CustomTranslationModel = model.ModelAlias,
                CustomTranslationApiProtocol = ModelApiProtocols.ChatCompletions,
                ManagedModelPackageId = model.Id, ManagedRuntimeBackend = backend, ManagedRuntimeEnabled = true,
                ManagedRuntimeDevice = device, RuntimeAllowMirrors = allowMirrors, RuntimeMirrorPrefixes = mirrorPrefixes,
                InitialSetupCompleted = true
            };
            await VerifyConfiguredModelAsync(configured, cancellation);
            configured = configured with { ModelConfiguration = (before.ModelConfiguration ?? new()) with
            {
                CurrentModel = new(ModelConfiguration.Managed, model.Id), VerifiedManagedModelId = model.Id,
                PendingModel = null, MigrationNotice = ""
            } };
            cancellation.ThrowIfCancellationRequested();
            // Serialize the compare-and-save with language, browser and settings transitions.
            await _settingsTransitionGate.WaitAsync(cancellation);
            try
            {
                if (Settings != before)
                    throw new ProviderException("initial_setup_changed", "配置在下载期间发生变化，请重新应用模型；现有配置未被覆盖。");
                await SettingsStore.SaveAsync(configured.Normalize(), cancellation);
                Settings = configured.Normalize();
            }
            finally { _settingsTransitionGate.Release(); }
        }
        catch
        {
            // An uncommitted model must not remain running after Skip/Cancel. A previous
            // managed configuration is retained and can start again on its next request.
            if (attemptedStart && Settings == before && !IsShuttingDown)
            {
                try { await ManagedModels.StopAsync(_lifetime.Token); }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            }
            throw;
        }
        finally { IsInitialSetupActive = previouslyActive; _initialModelGate.Release(); }
    }

    private async Task VerifyConfiguredModelAsync(AppSettings configured, CancellationToken cancellation)
    {
        if (ManagedMultimodalModels.TryGet(configured.ManagedModelPackageId, out var model))
            configured = configured with { CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint,
                CustomTranslationModel = model.ModelAlias, CustomTranslationApiProtocol = ModelApiProtocols.ChatCompletions };
        using var verification = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        verification.CancelAfter(TimeSpan.FromMinutes(4));
        // An owned loopback runtime never needs a cloud account credential.
        var localSecrets = new ProfileSecretStore(SecretStore, "");
        var translator = new ChatCompatibleTranslationProvider(_imageAnalysisClient, localSecrets, () => configured);
        var ocr = new ChatCompatibleOcrProvider(_imageAnalysisClient, localSecrets, () => configured, translator);
        await ModelCapabilityProbe.VerifyVisionAsync(ocr, verification.Token);
        await ModelCapabilityProbe.VerifyTextAsync(translator, verification.Token);
    }
}

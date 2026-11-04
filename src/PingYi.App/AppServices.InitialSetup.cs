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
    public async Task<AppSettings> ConfigureLocalModelAutomaticallyAsync(bool allowMirrors, string mirrorPrefixes,
        IProgress<ManagedModelProgress> progress, CancellationToken token)
    {
        await ConfigureInitialModelAsync(ManagedMultimodalModels.Recommended, "auto", "auto", allowMirrors,
            mirrorPrefixes, progress, token, automatic: true);
        return Settings;
    }

    public Task ConfigureInitialModelAsync(ManagedMultimodalModel model, string backend,
        IProgress<ManagedModelProgress> progress, CancellationToken token) =>
        ConfigureInitialModelAsync(model, backend, Settings.ManagedRuntimeDevice, Settings.RuntimeAllowMirrors,
            Settings.RuntimeMirrorPrefixes, progress, token);

    public async Task ConfigureInitialModelAsync(ManagedMultimodalModel model, string backend, string device,
        bool allowMirrors, string mirrorPrefixes, IProgress<ManagedModelProgress> progress, CancellationToken token,
        bool automatic = false)
    {
        ObjectDisposedException.ThrowIf(IsShuttingDown, this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var cancellation = lifetime.Token;
        await _initialModelGate.WaitAsync(cancellation);
        var previouslyActive = IsInitialSetupActive;
        IsInitialSetupActive = true;
        var before = Settings;
        if (automatic)
        {
            model = ManagedMultimodalModels.TryGet(before.ManagedModelPackageId, out var existing)
                ? existing : ManagedMultimodalModels.Recommended;
            backend = device = "auto";
        }
        var attemptedStart = false;
        var wasRunning = ManagedModels.HasRunningOwnedBackend;
        try
        {
            if (automatic)
            {
                progress.Report(new("detecting", "正在检测本机显卡与可用后端… / Detecting local GPUs and runtimes…", 0, 0, true));
                await ManagedModels.Runtimes.DetectAsync(true, cancellation);
            }
            progress.Report(new("preparing", $"正在准备 {model.LocalizedDisplayName}… / Preparing the local model…", 0, 0, true));
            // Installed Vulkan/CPU do not require an online update just to configure a model.
            if (await ManagedModels.Runtimes.UsableInstalledAsync(backend, device, cancellation) is null)
                await ManagedModels.Runtimes.InstallAsync(backend, device, allowMirrors,
                    mirrorPrefixes.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), progress, cancellation);
            await ManagedModels.DownloadAsync(model, progress, cancellation);
            attemptedStart = true;
            if (automatic)
            {
                var startup = InvalidateManagedRuntimeStartup();
                CancelAndRelease(startup.Cancellation, startup.Task);
                await startup.Task;
                wasRunning |= ManagedModels.HasRunningOwnedBackend;
                await ManagedModels.StopAsync(cancellation);
            }
            await ManagedModels.EnsureStartedAsync(model, backend, progress, cancellation, device);
            var configured = before with
            {
                CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint, CustomTranslationModel = model.ModelAlias,
                CustomTranslationApiProtocol = ModelApiProtocols.ChatCompletions,
                ManagedModelPackageId = model.Id, ManagedRuntimeBackend = backend, ManagedRuntimeEnabled = true,
                ManagedRuntimeDevice = device, RuntimeAllowMirrors = allowMirrors, RuntimeMirrorPrefixes = mirrorPrefixes,
                InitialSetupCompleted = true
            };
            progress.Report(new("verifying", "正在验证截图识字与翻译… / Verifying recognition and translation…", 0, 0, true));
            if (await VerifyManagedRuntimeWithFallbackAsync(model, configured, progress, cancellation))
                configured = configured with { ManagedRuntimeBackend = "cpu", ManagedRuntimeDevice = "auto" };
            configured = configured with { ModelConfiguration = (before.ModelConfiguration ?? new()) with
            {
                CurrentModel = new(ModelConfiguration.Managed, model.Id), VerifiedManagedModelId = model.Id,
                PendingModel = null, MigrationNotice = ""
            } };
            cancellation.ThrowIfCancellationRequested();
            progress.Report(new("saving", "正在保存并设为当前模型… / Saving and selecting the current model…", 0, 0, true));
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
            progress.Report(new("ready", ManagedModels.CurrentRuntimeDescription, 0, 0));
        }
        catch (Exception failure)
        {
            if (attemptedStart && Settings == before && !IsShuttingDown)
            {
                using var recovery = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                recovery.CancelAfter(ManagedRuntimeReadiness.OperationTimeout);
                try
                {
                    await ManagedModels.StopAsync(recovery.Token);
                    if (wasRunning && RuntimePolicy.HasConfiguredManagedRuntime(before) &&
                        ManagedMultimodalModels.TryGet(before.ManagedModelPackageId, out var previous))
                        await ManagedModels.EnsureStartedAsync(previous, before.ManagedRuntimeBackend,
                            progress, recovery.Token, before.ManagedRuntimeDevice);
                }
                catch (Exception restoreFailure)
                {
                    throw new AggregateException("自动配置未完成，原设置保留；恢复原模型也未完成。 / Setup failed; settings retained, but runtime recovery also failed.", failure, restoreFailure);
                }
            }
            throw;
        }
        finally { IsInitialSetupActive = previouslyActive; _initialModelGate.Release(); }
    }

    private Task<bool> VerifyManagedRuntimeWithFallbackAsync(ManagedMultimodalModel model, AppSettings configured,
        IProgress<ManagedModelProgress> progress, CancellationToken cancellation) =>
        AutomaticRuntimeVerification.VerifyAsync(configured.ManagedRuntimeBackend == "auto" &&
            configured.ManagedRuntimeDevice == "auto" && ManagedModels.IsRunningOnGpu,
            token => VerifyConfiguredModelAsync(configured, token), async token =>
        {
            progress.Report(new("fallback", "显卡推理验证未通过，正在切换 CPU 并重新验证… / GPU verification failed; switching to CPU and verifying again…", 0, 0, true));
            await ManagedModels.StopAsync(token);
            await ManagedModels.EnsureStartedAsync(model, "cpu", progress, token, "auto");
        }, cancellation);

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

using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public sealed partial class AppServices
{
    private int _runtimeMaintenance;
    internal bool IsRuntimeMaintenance => Volatile.Read(ref _runtimeMaintenance) != 0;

    /// <summary>Manual action: install if missing, test using the existing model, then save.</summary>
    public async Task SwitchRuntimeAsync(string backend, string device, bool allowMirrors, string mirrors,
        IProgress<ManagedModelProgress> progress, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(IsShuttingDown, this);
        backend = ManagedRuntimeBackends.Normalize(backend);
        if (device != "auto" && !RuntimeDeviceChoice.TryParse(device, out _, out _, out _))
            throw new ProviderException("runtime_device_invalid", "请重新检测并选择执行显卡。 / Detect and select the execution GPU again.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _initialModelGate.WaitAsync(lifetime.Token);
        var priorSetup = IsInitialSetupActive;
        IsInitialSetupActive = true; Volatile.Write(ref _runtimeMaintenance, 1);
        var before = Settings;
        var model = RuntimePolicy.HasConfiguredManagedRuntime(before) &&
            ManagedMultimodalModels.TryGet(before.ManagedModelPackageId, out var configuredModel) ? configuredModel : null;
        var next = before with { ManagedRuntimeBackend = backend, ManagedRuntimeDevice = device,
            RuntimeAllowMirrors = allowMirrors, RuntimeMirrorPrefixes = mirrors };
        var wasRunning = false;
        var activationStarted = false;
        InstalledRuntime? runtime = null;
        try
        {
            // Keep a currently loading/ready old server untouched during preparation.
            wasRunning = ManagedModels.HasRunningOwnedBackend;
            await RuntimeChangeTransaction.ApplyAsync(async cancellation =>
            {
                runtime = await ManagedModels.Runtimes.RecommendedInstalledAsync(backend, device, cancellation)
                    ?? await ManagedModels.Runtimes.InstallAsync(backend, device, allowMirrors,
                        mirrors.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), progress, cancellation);
                // Probe existing files as well as newly installed packages before activation.
                await ManagedModels.Runtimes.ValidateInstalledAsync(runtime, device, cancellation);
                if (model is not null && !(await ManagedModels.GetStatusAsync(model, cancellation)).IsInstalled)
                    throw new ProviderException("runtime_model_missing", "模型尚未完整安装，请先完成模型配置；切换后端不会重新下载模型。 / Complete model setup first; backend switching does not download weights.");
            }, async cancellation =>
            {
                activationStarted = true;
                wasRunning |= ManagedModels.HasRunningOwnedBackend;
                var startup = InvalidateManagedRuntimeStartup();
                CancelAndRelease(startup.Cancellation, startup.Task);
                await startup.Task;
                if (model is not null)
                {
                    await ManagedModels.EnsureStartedAsync(model, runtime!.Backend, progress, cancellation, device);
                    progress.Report(new ManagedModelProgress("runtime-verify", "正在验证新后端的固定 OCR／翻译样例… / Testing OCR and translation on the new backend…", 0, 0, true));
                    await VerifyConfiguredModelAsync(next, cancellation);
                }
                else await ManagedModels.StopAsync(cancellation);
            }, async cancellation =>
            {
                await _settingsTransitionGate.WaitAsync(cancellation);
                try
                {
                    if (Settings != before) throw new ProviderException("runtime_settings_changed", "设置已变化，本次切换未保存。 / Settings changed; the switch was not saved.");
                    await SettingsStore.SaveAsync(next.Normalize(), cancellation);
                    Settings = next.Normalize();
                }
                finally { _settingsTransitionGate.Release(); }
            }, async () =>
            {
                if (!activationStarted || IsShuttingDown) return;
                using var recovery = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                recovery.CancelAfter(ManagedRuntimeReadiness.OperationTimeout);
                await ManagedModels.StopAsync(recovery.Token);
                if (wasRunning && model is not null && Settings == before)
                    await ManagedModels.EnsureStartedAsync(model, before.ManagedRuntimeBackend, progress, recovery.Token, before.ManagedRuntimeDevice);
            }, lifetime.Token);
            progress.Report(new ManagedModelProgress("runtime-switched", model is null
                ? "后端偏好已保存；尚未配置托管模型，请继续模型配置。 / Backend preference saved; configure a managed model to run inference."
                : "后端已切换，OCR／翻译测试通过。旧文件保留，可手动卸载已下载的旧后端。 / Switched and verified. Downloaded previous backends can now be uninstalled.", 0, 0));
        }
        finally { Volatile.Write(ref _runtimeMaintenance, 0); IsInitialSetupActive = priorSetup; _initialModelGate.Release(); }
    }

    public async Task<RuntimeRemovalResult> UninstallRuntimeAsync(string backend, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(IsShuttingDown, this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _initialModelGate.WaitAsync(lifetime.Token);
        var priorSetup = IsInitialSetupActive;
        IsInitialSetupActive = true; Volatile.Write(ref _runtimeMaintenance, 1);
        try
        {
            if (backend == Settings.ManagedRuntimeBackend ||
                (RuntimeDeviceChoice.TryParse(Settings.ManagedRuntimeDevice, out var selected, out _, out _) && selected == backend))
                throw new ProviderException("runtime_in_use", "请先安装并切换到另一个后端，再卸载当前保存的后端。 / Switch to another backend before uninstalling the saved backend.");
            // Do not stop a loading server as a side effect of checking whether removal is safe.
            await ManagedRuntimeStartupTask.WaitAsync(lifetime.Token);
            return await ManagedModels.RemoveDownloadedRuntimeAsync(backend, lifetime.Token);
        }
        finally { Volatile.Write(ref _runtimeMaintenance, 0); IsInitialSetupActive = priorSetup; _initialModelGate.Release(); }
    }
}

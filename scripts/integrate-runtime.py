"""One-use, exact-anchor integration of the reviewed runtime changes. Removed from final source."""
from pathlib import Path


def edit(path, before, after, count=1):
    p = Path(path)
    text = p.read_text(encoding='utf-8-sig')
    if text.count(before) != count:
        raise RuntimeError(f'{path}: expected {count} anchors, found {text.count(before)}: {before[:100]}')
    p.write_text(text.replace(before, after), encoding='utf-8')


p = 'src/PingYi.Core/ManagedMultimodalModels.cs'
edit(p, '"auto" => "Auto detect (recommended)",', '"auto" => "Auto detect (recommended)",\n        "cuda12" => "NVIDIA · CUDA 12",\n        "cuda13" => "NVIDIA · CUDA 13",\n        "rocm" => "AMD · ROCm / HIP",')
edit(p, '"auto" => "Prefer Vulkan for AMD, NVIDIA, or Intel GPUs, then fall back to CPU automatically.",', '"auto" => "Detect hardware and prefer compatible installed CUDA/ROCm; otherwise try Vulkan, then CPU. New downloads require confirmation.",\n        "cuda12" => "CUDA 12 for compatible NVIDIA GPUs/drivers; Windows uses 12.4, Linux 12.8 in the bundled catalogue. No silent fallback.",\n        "cuda13" => "CUDA 13 for Turing or newer NVIDIA GPUs and compatible drivers. No silent fallback.",\n        "rocm" => "AMD ROCm/HIP; actual device and driver support must pass a runtime probe. No silent fallback.",')
edit(p, '优先使用 AMD、NVIDIA、Intel 均可用的 Vulkan，失败后自动回退 CPU。', '识别显卡，优先已安装的兼容 CUDA／ROCm，否则尝试 Vulkan、CPU。新后端只在确认下载后安装。')
edit(p, '    public static IReadOnlyList<ManagedRuntimeBackend> All { get; } = [Auto, Vulkan, Cpu];', '''    public static ManagedRuntimeBackend Cuda12 { get; } = new("cuda12", "NVIDIA · CUDA 12", "兼容 NVIDIA 显卡。内置清单 Windows 为 CUDA 12.4，Linux 为 12.8；手动选择失败不静默回退。");
    public static ManagedRuntimeBackend Cuda13 { get; } = new("cuda13", "NVIDIA · CUDA 13", "面向 Turing 及更新架构，需兼容驱动；手动选择失败不静默回退。");
    public static ManagedRuntimeBackend Rocm { get; } = new("rocm", "AMD · ROCm / HIP", "AMD 专用后端；以实际设备枚举和模型验证为准，手动选择失败不静默回退。");
    public static IReadOnlyList<ManagedRuntimeBackend> All { get; } = [Auto, Cuda12, Cuda13, Rocm, Vulkan, Cpu];''')
p = 'src/PingYi.Core/AppSettings.cs'
edit(p, 'public const int CurrentSchemaVersion = 11;', 'public const int CurrentSchemaVersion = 12;')
edit(p, '    public bool ManagedRuntimeEnabled { get; init; }', '''    public string ManagedRuntimeDevice { get; init; } = RuntimeDeviceChoice.Automatic;
    public bool RuntimeAllowMirrors { get; init; }
    public string RuntimeMirrorPrefixes { get; init; } = string.Empty;
    public bool ManagedRuntimeEnabled { get; init; }''')
edit(p, '            ManagedRuntimeBackend = ManagedRuntimeBackends.Normalize(ManagedRuntimeBackend),', '''            ManagedRuntimeBackend = ManagedRuntimeBackends.Normalize(ManagedRuntimeBackend),
            ManagedRuntimeDevice = RuntimeDeviceChoice.Normalize(ManagedRuntimeDevice),
            RuntimeMirrorPrefixes = RuntimeMirrorPrefixes?.Trim() ?? string.Empty,''')
p = 'src/PingYi.Infrastructure/ManagedModelService.cs'
edit(p, '    private string? _runningBackendId;', '''    private string? _runningBackendId;
    private string _runningSelection = "auto";
    private string? _runningExecutable;
    public RuntimeManager Runtimes { get; }
    public string CurrentRuntimeDescription { get; private set; } = "未启动 / Not running";''')
edit(p, '        _paths = paths;', '        _paths = paths;\n        Runtimes = new RuntimeManager(paths);')
edit(p, '''        string backendId,
        IProgress<ManagedModelProgress>? progress = null,
        CancellationToken cancellationToken = default)''', '''        string backendId,
        IProgress<ManagedModelProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string deviceSelection = "auto")''')
edit(p, '            var normalizedBackend = ManagedRuntimeBackends.Normalize(backendId);', '''            var normalizedBackend = ManagedRuntimeBackends.Normalize(backendId);
            if (deviceSelection != "auto" && !RuntimeDeviceChoice.TryParse(deviceSelection, out _, out _, out _))
                throw new ProviderException("runtime_device_invalid", "执行显卡设置无效，请重新检测。");
            var selectedRuntime = await Runtimes.RecommendedInstalledAsync(normalizedBackend, deviceSelection, cancellationToken);''')
edit(p, '''            if (_ownedProcess is { HasExited: false } &&
                string.Equals(_runningModelId''', '''            if (_ownedProcess is { HasExited: false } && _runningSelection == deviceSelection &&
                _runningExecutable == (selectedRuntime?.Executable ?? _runningExecutable) &&
                string.Equals(_runningModelId''')
edit(p, '                    return $"本机模型服务已通过 {DescribeBackend(_runningBackendId)} 运行";', '                    return CurrentRuntimeDescription;')
edit(p, '''                var ownedProcessMatches = _ownedProcess is not null &&
                                          string.Equals''', '''                if (_ownedProcess is null && deviceSelection != "auto")
                    throw new ProviderException("runtime_external_device", "端口由外部服务提供，无法为它更换执行显卡；请在该服务中设置或停止它后重试。");
                var ownedProcessMatches = _ownedProcess is not null && _runningSelection == deviceSelection &&
                                          _runningExecutable == (selectedRuntime?.Executable ?? _runningExecutable) &&
                                          string.Equals''')
edit(p, '                        : $"本机模型服务已通过 {DescribeBackend(_runningBackendId)} 运行";', '                        : CurrentRuntimeDescription;')
edit(p, '''            foreach (var runtime in GetRuntimeCandidates(normalizedBackend))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var backendName = runtime.IsGpu ? "Vulkan 通用显卡" : "CPU";''', '''            var hardware = normalizedBackend == "auto" ? await Runtimes.DetectAsync(false, cancellationToken) : [];
            var candidateIds = RuntimeHardwarePolicy.Candidates(normalizedBackend, deviceSelection, hardware, OperatingSystem.IsWindows());
            foreach (var candidateId in candidateIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var installed = Runtimes.Find(candidateId);
                if (installed is null) continue;
                var runtime = new RuntimeCandidate(installed.Executable, candidateId, installed.Tag);
                var backendName = DescribeBackend(candidateId);''')
edit(p, '                    StartProcess(runtime, model);', '''                    if (runtime.IsGpu)
                    {
                        var devices = await GpuInventory.ListDevicesAsync(runtime.ExecutablePath, runtime.BackendId, cancellationToken);
                        var device = RuntimeDeviceChoice.Resolve(deviceSelection, runtime.BackendId, devices)
                            ?? throw new ProviderException("runtime_no_gpu", "此后端未枚举到可用显卡。");
                        runtime = runtime with { Device = device };
                    }
                    StartProcess(runtime, model);''')
edit(p, '''                    _runningBackendId = runtime.IsGpu
                        ? ManagedRuntimeBackends.Vulkan.Id
                        : ManagedRuntimeBackends.Cpu.Id;
                    return runtime.IsGpu
                        ? "模型已通过 Vulkan 显卡后端启动"
                        : normalizedBackend == ManagedRuntimeBackends.Cpu.Id
                            ? "模型已通过 CPU 后端启动"
                            : "显卡后端不可用，已自动回退 CPU 并启动";''', '''                    _runningBackendId = runtime.BackendId;
                    _runningSelection = deviceSelection;
                    _runningExecutable = runtime.ExecutablePath;
                    CurrentRuntimeDescription = $"{backendName} · {runtime.Version}" +
                        (runtime.Device is null ? "" : $" · {runtime.Device.Id} · {runtime.Device.Name}") +
                        (lastError is null ? "" : " · 已回退 / fallback");
                    return CurrentRuntimeDescription;''')
edit(p, '                    if (runtime.IsGpu && normalizedBackend == ManagedRuntimeBackends.Auto.Id)', '                    if (runtime.IsGpu && normalizedBackend == ManagedRuntimeBackends.Auto.Id && deviceSelection == "auto")')
edit(p, '                            "Vulkan 启动失败，正在自动回退 CPU…",', '                            $"{backendName} 启动失败，正在尝试下一个兼容后端…",')
text = Path(p).read_text(encoding='utf-8')
start = text.index('    private IReadOnlyList<RuntimeCandidate> GetRuntimeCandidates()')
end = text.index('    private void StartProcess(', start)
edit(p, text[start:end], '''    private IReadOnlyList<RuntimeCandidate> GetRuntimeCandidates() =>
        ManagedRuntimeBackends.All.Where(backend => backend.Id != "auto")
            .Select(backend => Runtimes.Find(backend.Id)).Where(runtime => runtime is not null)
            .Select(runtime => new RuntimeCandidate(runtime!.Executable, runtime.Backend, runtime.Tag)).ToArray();

''')
edit(p, '        AddArgument(startInfo, "-m",', '''        RuntimeProcessProbe.ConfigureEnvironment(startInfo);
        if (runtime.Device is { } device)
        {
            AddArgument(startInfo, "--device", device.Id);
            AddArgument(startInfo, "--split-mode", "none");
        }
        AddArgument(startInfo, "-m",''')
edit(p, '        AddArgument(startInfo, "--n-gpu-layers", runtime.IsGpu ? "99" : "0");', '        AddArgument(startInfo, "--n-gpu-layers", runtime.IsGpu ? (runtime.Version == "bundled" ? "99" : "auto") : "0");')
edit(p, '        _runningModelId = _runningBackendId = null;', '''        _runningModelId = _runningBackendId = _runningExecutable = null;
        _runningSelection = "auto";
        CurrentRuntimeDescription = "已停止 / Stopped";''')
edit(p, '''    private static string DescribeBackend(string? backendId) =>
        string.Equals(backendId, ManagedRuntimeBackends.Vulkan.Id, StringComparison.OrdinalIgnoreCase)
            ? "Vulkan 通用显卡"
            : "CPU";''', '    private static string DescribeBackend(string? backendId) => ManagedRuntimeBackends.Get(backendId).LocalizedDisplayName;')
edit(p, '''            await _lifetime.CancelAsync();
            await _operationGate.WaitAsync();
            try { await StopOwnedProcessCoreAsync(); }
            finally { _operationGate.Release(); }''', '''            await _lifetime.CancelAsync();
            await ShutdownWork.RunAllAsync(
                () => Runtimes.DisposeAsync().AsTask(),
                async () =>
                {
                    await _operationGate.WaitAsync();
                    try { await StopOwnedProcessCoreAsync(); }
                    finally { _operationGate.Release(); }
                });''')
edit(p, '    private sealed record RuntimeCandidate(string ExecutablePath, bool IsGpu);', '''    private sealed record RuntimeCandidate(string ExecutablePath, string BackendId, string Version, RuntimeDevice? Device = null)
    {
        public bool IsGpu => BackendId != "cpu";
    }''')
p = 'src/PingYi.App/AppServices.cs'
edit(p, '    private readonly HttpClient _httpClient;', '''    private readonly HttpClient _httpClient;
    private readonly HttpClient _inferenceClient = new(new SocketsHttpHandler
        { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(8) }) { Timeout = Timeout.InfiniteTimeSpan };''')
edit(p, 'CustomTranslationProvider = new ChatCompatibleTranslationProvider(httpClient,', 'CustomTranslationProvider = new ChatCompatibleTranslationProvider(_inferenceClient,')
edit(p, 'LocalVlmOcrProvider = new ChatCompatibleOcrProvider(httpClient,', 'LocalVlmOcrProvider = new ChatCompatibleOcrProvider(_inferenceClient,')
edit(p, 'new ChatCompatibleTranslationProvider(_httpClient,', 'new ChatCompatibleTranslationProvider(_inferenceClient,', 2)
edit(p, 'new ChatCompatibleOcrProvider(_httpClient,', 'new ChatCompatibleOcrProvider(_inferenceClient,')
edit(p, '$"{model.Id}|{ManagedRuntimeBackends.Normalize(settings.ManagedRuntimeBackend)}";', '$"{model.Id}|{ManagedRuntimeBackends.Normalize(settings.ManagedRuntimeBackend)}|{settings.ManagedRuntimeDevice}";')
edit(p, 'StartManagedRuntimeCoreAsync(model, settings.ManagedRuntimeBackend, _managedRuntimeStartupCancellation.Token)', 'StartManagedRuntimeCoreAsync(model, settings.ManagedRuntimeBackend, settings.ManagedRuntimeDevice, _managedRuntimeStartupCancellation.Token)')
edit(p, 'StartManagedRuntimeCoreAsync(ManagedMultimodalModel model, string backend,', 'StartManagedRuntimeCoreAsync(ManagedMultimodalModel model, string backend, string device,')
edit(p, 'new Progress<ManagedModelProgress>(value => Volatile.Write(ref _managedProgress, value)), deadline.Token);', 'new Progress<ManagedModelProgress>(value => Volatile.Write(ref _managedProgress, value)), deadline.Token, device);')
edit(p, '_httpClient.Dispose(); _imageAnalysisClient.Dispose();', '_httpClient.Dispose(); _inferenceClient.Dispose(); _imageAnalysisClient.Dispose();')
p = 'src/PingYi.App/AppServices.InitialSetup.cs'
edit(p, '''    public async Task ConfigureInitialModelAsync(ManagedMultimodalModel model, string backend,
        IProgress<ManagedModelProgress> progress, CancellationToken token)
    {''', '''    public Task ConfigureInitialModelAsync(ManagedMultimodalModel model, string backend,
        IProgress<ManagedModelProgress> progress, CancellationToken token) =>
        ConfigureInitialModelAsync(model, backend, Settings.ManagedRuntimeDevice, Settings.RuntimeAllowMirrors,
            Settings.RuntimeMirrorPrefixes, progress, token);

    public async Task ConfigureInitialModelAsync(ManagedMultimodalModel model, string backend, string device,
        bool allowMirrors, string mirrorPrefixes, IProgress<ManagedModelProgress> progress, CancellationToken token)
    {''')
edit(p, '''            if (!ManagedModels.HasBundledRuntime)
                throw new ProviderException("managed_runtime_unavailable", "此安装目录缺少 llama.cpp 运行时，请安装完整软件包或使用已有本机服务／轻量模式。");''', '''            await ManagedModels.Runtimes.InstallAsync(backend, device, allowMirrors,
                mirrorPrefixes.Split('\\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), progress, cancellation);'''.replace("'\\\\n'", "'\\n'"))
edit(p, 'await ManagedModels.EnsureStartedAsync(model, backend, progress, cancellation);', 'await ManagedModels.EnsureStartedAsync(model, backend, progress, cancellation, device);')
edit(p, 'ManagedModelPackageId = model.Id, ManagedRuntimeBackend = backend, ManagedRuntimeEnabled = true,', '''ManagedModelPackageId = model.Id, ManagedRuntimeBackend = backend, ManagedRuntimeEnabled = true,
                ManagedRuntimeDevice = device, RuntimeAllowMirrors = allowMirrors, RuntimeMirrorPrefixes = mirrorPrefixes,''')
p = 'src/PingYi.Core/TranslationFallback.cs'
edit(p, '        CancellationToken cancellationToken = default)', '''        CancellationToken cancellationToken = default,
        TimeSpan? primaryTimeout = null, TimeSpan? fallbackTimeout = null)''')
edit(p, '            var availability = await primary.GetAvailabilityAsync(cancellationToken);', '''            using var primaryDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            primaryDeadline.CancelAfter(primaryTimeout ?? TimeSpan.FromMinutes(2));
            var availability = await primary.GetAvailabilityAsync(primaryDeadline.Token);''')
edit(p, 'var result = await primary.TranslateAsync(request, cancellationToken);', 'var result = await primary.TranslateAsync(request, primaryDeadline.Token);')
edit(p, '            primaryFailure is not OperationCanceledException &&\n            !cancellationToken.IsCancellationRequested &&', '            !cancellationToken.IsCancellationRequested &&')
edit(p, '            var fallbackRequest = ResolveFallbackRequest(offlineFallback.Metadata, request);', '''            // An internal HTTP/phase timeout is not a user cancellation. Keep diagnostics authored.
            var reportedFailure = primaryFailure is OperationCanceledException
                ? new ProviderException("translation_primary_timeout", "主翻译引擎等待超时，尝试轻量回退。") : primaryFailure;
            var fallbackRequest = ResolveFallbackRequest(offlineFallback.Metadata, request);''')
edit(p, '{primaryFailure.Message}', '{reportedFailure.Message}', 2)
edit(p, '                    primaryFailure);', '                    reportedFailure);', 2)
edit(p, '                var fallbackAvailability = await offlineFallback.GetAvailabilityAsync(cancellationToken);', '''                using var fallbackDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                fallbackDeadline.CancelAfter(fallbackTimeout ?? TimeSpan.FromMinutes(2));
                var fallbackAvailability = await offlineFallback.GetAvailabilityAsync(fallbackDeadline.Token);''')
edit(p, 'await offlineFallback.TranslateAsync(fallbackRequest, cancellationToken);', 'await offlineFallback.TranslateAsync(fallbackRequest, fallbackDeadline.Token);')
edit(p, '            catch (Exception fallbackFailure) when (fallbackFailure is not OperationCanceledException)', '''            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ProviderException("translation_fallback_timeout", "主翻译引擎与轻量回退均未及时完成，可复制原文后重试。", reportedFailure);
            }
            catch (Exception fallbackFailure) when (fallbackFailure is not OperationCanceledException)''')
edit(p, '''        }
    }

    private static TranslationRequest? ResolveFallbackRequest''', '''        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException("translation_timeout", "翻译引擎等待超时，可复制原文后重试。");
        }
    }

    private static TranslationRequest? ResolveFallbackRequest''')
p = 'src/PingYi.App/CaptureCoordinator.Text.cs'
edit(p, '''            var execution = await WithTimeoutAsync(token => TranslationFallback.ExecuteAsync(translator, services.ArgosProvider, request, token),
                TranslationTimeout, operation.Token, "translation_timeout", "翻译超时，可复制原文或重试。");''', '''            var execution = await TranslationFallback.ExecuteAsync(translator, services.ArgosProvider, request,
                operation.Token, primaryTimeout: TranslationTimeout, fallbackTimeout: TranslationTimeout);''')
edit(p, 'public sealed partial class CaptureCoordinator\n{', 'public sealed partial class CaptureCoordinator\n{\n    private readonly OcrRetryCache _ocrRetryCache = new();')
edit(p, '''        OcrResult result;
        try
        {''', '''        OcrResult result;
        var reused = _ocrRetryCache.TryGetForTranslationRetry(image, settings, out var retryOcr, out var retryLabel);
        if (reused) { result = retryOcr; label = retryLabel; }
        else
        try
        {''')
edit(p, '''        window.SetSource(result, label);
        try''', '''        window.SetSource(result, label);
        _ocrRetryCache.Remember(image, settings, result, label);
        try''')
edit(p, '''            EnsureCurrent(operation);
            var name = UiText.ProviderName(execution.Provider.Id''', '''            EnsureCurrent(operation);
            _ocrRetryCache.MarkTranslationComplete(image);
            var name = UiText.ProviderName(execution.Provider.Id''')
p = 'src/PingYi.App/SettingsWindow.axaml.cs'
edit(p, '        LoadSettings();\n        Opened +=', '        LoadSettings();\n        InitializeRuntimeHardware();\n        Opened +=')
edit(p, '            ManagedRuntimeBackend = SelectedManagedRuntimeBackendId,', '''            ManagedRuntimeBackend = SelectedManagedRuntimeBackendId,
            ManagedRuntimeDevice = _runtimeHardware?.SelectedDevice ?? current.ManagedRuntimeDevice,
            RuntimeAllowMirrors = _runtimeHardware?.AllowMirrors ?? current.RuntimeAllowMirrors,
            RuntimeMirrorPrefixes = _runtimeHardware?.MirrorPrefixes ?? current.RuntimeMirrorPrefixes,''')
p = 'src/PingYi.App/SettingsWindow.Models.cs'
edit(p, 'await _services.ConfigureInitialModelAsync(model, SelectedManagedRuntimeBackendId, progress, token);', '''await _services.ConfigureInitialModelAsync(model, SelectedManagedRuntimeBackendId,
            _runtimeHardware?.SelectedDevice ?? "auto", _runtimeHardware?.AllowMirrors ?? false,
            _runtimeHardware?.MirrorPrefixes ?? "", progress, token);''')
edit(p, 'new Progress<ManagedModelProgress>(UpdateManagedModelProgress));', 'new Progress<ManagedModelProgress>(UpdateManagedModelProgress), deviceSelection: _services.Settings.ManagedRuntimeDevice);')
edit(p, '        ManagedModelCombo.IsEnabled = ManagedRuntimeBackendCombo.IsEnabled', '        _runtimeHardware?.SetParentBusy(busy);\n        ManagedModelCombo.IsEnabled = ManagedRuntimeBackendCombo.IsEnabled')
p = 'src/PingYi.App/FirstRunSetupWindow.cs'
edit(p, '    private readonly bool _hasRuntime;', '''    private readonly bool _hasRuntime;
    private RuntimeSetupPanel? _runtimeHardware;
    private readonly StackPanel _runtimeHost = new() { Spacing = 8 };''')
edit(p, '        }) { }', '''        })
    {
        _runtimeHardware = new RuntimeSetupPanel(services.ManagedModels.Runtimes, _backends, services.Settings,
            () => services.ManagedModels.CurrentRuntimeDescription);
        _runtimeHost.Children.Add(_runtimeHardware);
        _configure = (model, backend, progress, token) => services.ConfigureInitialModelAsync(model, backend,
            _runtimeHardware.SelectedDevice, _runtimeHardware.AllowMirrors, _runtimeHardware.MirrorPrefixes, progress, token);
        Opened += async (_, _) => await _runtimeHardware.RefreshAsync();
        Closed += (_, _) => _runtimeHardware.Cancel();
        Height = 820;
    }''')
edit(p, '                    _models, _backends, _details,', '                    _models, _backends, _runtimeHost, _details,')
edit(p, '        DownloadButton.IsEnabled = !busy && _hasRuntime;', '        _runtimeHardware?.SetParentBusy(busy);\n        DownloadButton.IsEnabled = !busy && _hasRuntime;')
p = 'tests/PingYi.Core.Tests/TranslationFallbackTests.cs'
edit(p, '''        var external = new StubProvider("custom-chat", fail: false, "external", cancel: true);
        var offline = new StubProvider("local-argos", fail: false, "offline");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TranslationFallback.ExecuteAsync(
                external,
                offline,
                new TranslationRequest("Hello", "en", "zh")));''', '''        var external = new StubProvider("custom-chat", fail: false, "external", cancel: true);
        var offline = new StubProvider("local-argos", fail: false, "offline");
        using var userCancellation = new CancellationTokenSource();
        userCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TranslationFallback.ExecuteAsync(
                external,
                offline,
                new TranslationRequest("Hello", "en", "zh"), userCancellation.Token));''')
p = 'src/PingYi.Infrastructure/Runtime/RuntimeManager.cs'
edit(p, 'internal partial class RuntimeJsonContext : JsonSerializerContext;', 'internal partial class RuntimeJsonContext : JsonSerializerContext { }')
# Remove development-only transfer machinery before committing the integrated source.
Path('.github/workflows/runtime-dev-snapshot.yml').unlink()
Path('.github/workflows/runtime-integration.yml').unlink()
Path(__file__).unlink()

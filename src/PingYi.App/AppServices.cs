using PingYi.Core;
using PingYi.Infrastructure;
using System.Text.Json.Nodes;

namespace PingYi.App;

public sealed partial class AppServices : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _imageAnalysisClient = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan };
    private ManagedModelProgress? _managedProgress;
    public IImageAnalysisProvider CreateImageAnalyzer(AppSettings snapshot) =>
        new ChatCompatibleImageAnalysisProvider(_imageAnalysisClient, SecretStore, snapshot);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _managedRuntimeLock = new();
    private readonly SemaphoreSlim _settingsTransitionGate = new(1, 1);
    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<ProviderAvailability> _managedRuntimeStartupTask = Task.FromResult(ProviderAvailability.Available);
    private CancellationTokenSource? _managedRuntimeStartupCancellation;
    private string _managedRuntimeConfiguration = string.Empty;
    private int _disposeState;
    private BrowserBridgeServer? _browserBridge;

    public void StartBrowserBridge()
    {
        if (_browserBridge is not null) return;
        var handler = new BrowserTranslationService(Providers, async token => { await WaitForManagedRuntimeAsync(token); });
        _browserBridge = new BrowserBridgeServer(BrowserWire.PipeName(AppEdition.IsComplete ? "complete" : "standard"),
            async (request, token) =>
            {
                if (request.Operation == "status") return new BrowserResponse(true, Status: handler.GetStatus(Settings));
                await _settingsTransitionGate.WaitAsync(token);
                try { return await handler.HandleAsync(request, Settings, token); }
                finally { _settingsTransitionGate.Release(); }
            });
    }

    private AppServices(AppDataPaths paths, ISettingsStore settingsStore, ISecretStore secretStore,
        AppSettings settings, EngineProcessClient engine, IGlobalHotkeyService hotkeyService,
        IScreenCaptureService screenCaptureService, IImageCropper imageCropper, HttpClient httpClient)
    {
        Paths = paths; SettingsStore = settingsStore; SecretStore = secretStore; Settings = settings;
        Engine = engine; HotkeyService = hotkeyService; ScreenCaptureService = screenCaptureService;
        ImageCropper = imageCropper; _httpClient = httpClient;
        ManagedModels = new ManagedModelService(paths);
        UpdateService = new GitHubReleaseUpdateService(httpClient);
        PaddleProvider = new PaddleOcrProvider(paths);
        ArgosProvider = new ArgosTranslationProvider(engine);
        BaiduOcrProvider = new BaiduOcrProvider(httpClient, secretStore);
        BaiduTranslationProvider = new BaiduTranslationProvider(httpClient, secretStore);
        GoogleOcrProvider = new GoogleCloudVisionOcrProvider(httpClient, secretStore);
        GoogleTranslationProvider = new GoogleCloudTranslationProvider(httpClient, secretStore);
        CustomTranslationProvider = new ChatCompatibleTranslationProvider(httpClient, secretStore, () => Settings);
        LocalVlmOcrProvider = new ChatCompatibleOcrProvider(httpClient, secretStore, () => Settings, CustomTranslationProvider);
        Providers = new ProviderRegistry(
            [LocalVlmOcrProvider, PaddleProvider, BaiduOcrProvider, GoogleOcrProvider],
            [CustomTranslationProvider, ArgosProvider, BaiduTranslationProvider, GoogleTranslationProvider]);
    }

    public AppDataPaths Paths { get; }
    public ISettingsStore SettingsStore { get; }
    public ISecretStore SecretStore { get; }
    public AppSettings Settings { get; private set; }
    public EngineProcessClient Engine { get; }
    public IGlobalHotkeyService HotkeyService { get; }
    public IScreenCaptureService ScreenCaptureService { get; }
    public IImageCropper ImageCropper { get; }
    public IQrCodeDecoder QrCodeDecoder { get; } = new ZxingQrCodeDecoder();
    public ProviderRegistry Providers { get; }
    public ManagedModelService ManagedModels { get; }
    public GitHubReleaseUpdateService UpdateService { get; }
    public PaddleOcrProvider PaddleProvider { get; }
    public ArgosTranslationProvider ArgosProvider { get; }
    public BaiduOcrProvider BaiduOcrProvider { get; }
    public BaiduTranslationProvider BaiduTranslationProvider { get; }
    public GoogleCloudVisionOcrProvider GoogleOcrProvider { get; }
    public GoogleCloudTranslationProvider GoogleTranslationProvider { get; }
    public ChatCompatibleTranslationProvider CustomTranslationProvider { get; }
    public ChatCompatibleOcrProvider LocalVlmOcrProvider { get; }
    public Task<ProviderAvailability> ManagedRuntimeStartupTask
    {
        get { lock (_managedRuntimeLock) return _managedRuntimeStartupTask; }
    }

    // Capture requests use immutable endpoint/model snapshots, including after upload consent.
    public ITranslationProvider CaptureTranslator(AppSettings snapshot) => snapshot.TranslationProviderId == "custom-chat"
        ? new ChatCompatibleTranslationProvider(_httpClient, SecretStore, () => snapshot)
        : Providers.GetTranslationProvider(snapshot.TranslationProviderId);
    public IOcrProvider CaptureOcr(AppSettings snapshot) => snapshot.OcrProviderId == "local-vlm-ocr"
        ? new ChatCompatibleOcrProvider(_httpClient, SecretStore, () => snapshot,
            new ChatCompatibleTranslationProvider(_httpClient, SecretStore, () => snapshot))
        : Providers.GetOcrProvider(snapshot.OcrProviderId);

    public Task ClearDownloadedTranslationModelsAsync(CancellationToken cancellationToken = default) =>
        Engine.CallAsync("delete_models", new JsonObject { ["scope"] = "translation" }, cancellationToken);

    public static async Task<AppServices> CreateAsync(CancellationToken cancellationToken = default)
    {
        var paths = new AppDataPaths();
        var settingsStore = new JsonSettingsStore(paths);
        var settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var secretStore = new PlatformSecretStore(paths);
        var engine = new EngineProcessClient(paths);
        var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PingYi/0.1");
        return new AppServices(paths, settingsStore, secretStore, settings, engine,
            GlobalHotkeyServiceFactory.Create(), ScreenCaptureServiceFactory.Create(), new SkiaImageCropper(), httpClient);
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposeState) != 0) return;
        if (!AppSettings.TryParseChatCompletionsEndpoint(settings.CustomTranslationEndpoint, out var endpoint))
            throw new ProviderException("custom_endpoint_invalid", "OpenAI 兼容接口地址无效。");
        if (!AppSettings.IsChatCompletionsTransportAllowed(endpoint))
            throw new ProviderException("custom_endpoint_insecure_transport", "远程自定义服务必须使用 HTTPS；只有本机回环地址可以使用 HTTP。");
        var normalized = settings.Normalize();
        await _settingsTransitionGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _disposeState) != 0) return;
            var previous = Settings;
            await SettingsStore.SaveAsync(normalized, cancellationToken);
            Settings = normalized;
            if (RuntimePolicy.UsesManagedRuntime(Settings)) WarmManagedRuntimeIfConfigured();
            else if (RuntimePolicy.UsesManagedRuntime(previous))
            {
                var previousStartup = InvalidateManagedRuntimeStartup();
                CancelAndRelease(previousStartup.Cancellation, previousStartup.Task);
                await ManagedModels.StopAsync(_lifetime.Token);
            }
        }
        finally { _settingsTransitionGate.Release(); }
    }

    public async Task<ProviderAvailability> WaitForManagedRuntimeAsync(CancellationToken cancellationToken = default,
        bool forImageAnalysis = false, IProgress<ManagedModelProgress>? progress = null)
    {
        while (true)
        {
            if (Volatile.Read(ref _disposeState) != 0) throw new ObjectDisposedException(nameof(AppServices));
            var settings = Settings;
            if (!(forImageAnalysis ? RuntimePolicy.HasConfiguredManagedRuntime(settings) : RuntimePolicy.UsesManagedRuntime(settings)))
                return ProviderAvailability.Available;
            if (!ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out var model))
                throw new ProviderException("managed_runtime_unavailable", "未找到已配置的本机大模型。请在设置中重新选择模型。");
            WarmManagedRuntimeIfConfigured(forImageAnalysis);
            var expectedConfiguration = BuildManagedRuntimeConfiguration(model, settings);
            string configuration;
            Task<ProviderAvailability> startupTask;
            lock (_managedRuntimeLock) { configuration = _managedRuntimeConfiguration; startupTask = _managedRuntimeStartupTask; }
            var wait = startupTask.WaitAsync(cancellationToken);
            while (!wait.IsCompleted && progress is not null)
            {
                if (Volatile.Read(ref _managedProgress) is { } update) progress.Report(update);
                await Task.WhenAny(wait, Task.Delay(1000, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
            }
            var availability = await wait;
            if (Volatile.Read(ref _disposeState) != 0) throw new ObjectDisposedException(nameof(AppServices));
            lock (_managedRuntimeLock)
            {
                if (!ReferenceEquals(startupTask, _managedRuntimeStartupTask) ||
                    !string.Equals(configuration, _managedRuntimeConfiguration, StringComparison.Ordinal) ||
                    !string.Equals(configuration, expectedConfiguration, StringComparison.Ordinal)) continue;
            }
            var current = Settings;
            if (!(forImageAnalysis ? RuntimePolicy.HasConfiguredManagedRuntime(current) : RuntimePolicy.UsesManagedRuntime(current)))
                return ProviderAvailability.Available;
            if (!ManagedMultimodalModels.TryGet(current.ManagedModelPackageId, out var currentModel) ||
                !string.Equals(BuildManagedRuntimeConfiguration(currentModel, current), expectedConfiguration, StringComparison.Ordinal)) continue;
            if (!availability.IsAvailable)
                throw new ProviderException("managed_runtime_unavailable", availability.Message ?? "本机大模型服务无法启动。");
            return availability;
        }
    }

    private void WarmManagedRuntimeIfConfigured(bool forImageAnalysis = false)
    {
        if (Volatile.Read(ref _disposeState) != 0) return;
        var settings = Settings;
        if (!(forImageAnalysis ? RuntimePolicy.HasConfiguredManagedRuntime(settings) : RuntimePolicy.UsesManagedRuntime(settings)) ||
            !ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out var model)) return;
        var configuration = BuildManagedRuntimeConfiguration(model, settings);
        CancellationTokenSource? previousCancellation;
        Task<ProviderAvailability>? previousTask;
        lock (_managedRuntimeLock)
        {
            if (Volatile.Read(ref _disposeState) != 0) return;
            if (string.Equals(_managedRuntimeConfiguration, configuration, StringComparison.Ordinal) && !_managedRuntimeStartupTask.IsCompleted) return;
            previousCancellation = _managedRuntimeStartupCancellation;
            previousTask = _managedRuntimeStartupTask;
            _managedRuntimeStartupCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _managedRuntimeConfiguration = configuration;
            _managedRuntimeStartupTask = StartManagedRuntimeCoreAsync(model, settings.ManagedRuntimeBackend, _managedRuntimeStartupCancellation.Token);
        }
        CancelAndRelease(previousCancellation, previousTask);
    }

    private static string BuildManagedRuntimeConfiguration(ManagedMultimodalModel model, AppSettings settings) =>
        $"{model.Id}|{ManagedRuntimeBackends.Normalize(settings.ManagedRuntimeBackend)}";
    private (CancellationTokenSource? Cancellation, Task<ProviderAvailability> Task) InvalidateManagedRuntimeStartup()
    {
        lock (_managedRuntimeLock)
        {
            var previous = (_managedRuntimeStartupCancellation, _managedRuntimeStartupTask);
            _managedRuntimeStartupCancellation = null; _managedRuntimeConfiguration = string.Empty;
            _managedRuntimeStartupTask = Task.FromResult(ProviderAvailability.Available);
            return previous;
        }
    }
    private static void CancelAndRelease(CancellationTokenSource? cancellation, Task<ProviderAvailability>? task)
    {
        if (cancellation is null) return;
        cancellation.Cancel();
        if (task is null || task.IsCompleted) { cancellation.Dispose(); return; }
        _ = task.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(), cancellation,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private async Task<ProviderAvailability> StartManagedRuntimeCoreAsync(ManagedMultimodalModel model, string backend,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ManagedRuntimeReadiness.OperationTimeout);
        try
        {
            Volatile.Write(ref _managedProgress, null);
            await ManagedModels.EnsureStartedAsync(model, backend,
                new Progress<ManagedModelProgress>(value => Volatile.Write(ref _managedProgress, value)), deadline.Token);
            return ProviderAvailability.Available;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new ProviderAvailability(false, "本机大模型启动超时，请在设置中检查运行状态。"); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return new ProviderAvailability(false, "本机大模型启动已取消。"); }
        catch (Exception exception) { return new ProviderAvailability(false, exception.Message); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0) { await _disposeCompletion.Task; return; }
        try
        {
            // Cancel transitions before waiting: a model download may otherwise hold a gate indefinitely.
            _lifetime.Cancel();
            if (_browserBridge is not null) await _browserBridge.DisposeAsync();
            await _settingsTransitionGate.WaitAsync();
            try
            {
                var previous = InvalidateManagedRuntimeStartup();
                CancelAndRelease(previous.Cancellation, previous.Task);
                await previous.Task;
                await HotkeyService.DisposeAsync();
                await ManagedModels.DisposeAsync();
                await Engine.DisposeAsync();
                await PaddleProvider.DisposeAsync();
                _httpClient.Dispose(); _imageAnalysisClient.Dispose();
            }
            finally { _settingsTransitionGate.Release(); }
        }
        finally { Volatile.Write(ref _disposeState, 2); _disposeCompletion.TrySetResult(); }
    }
}

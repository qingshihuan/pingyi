using PingYi.Core;
using PingYi.Infrastructure;
using System.Text.Json.Nodes;

namespace PingYi.App;

public sealed class AppServices : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _managedRuntimeLock = new();
    private readonly SemaphoreSlim _settingsTransitionGate = new(1, 1);
    private readonly TaskCompletionSource _disposeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<ProviderAvailability> _managedRuntimeStartupTask =
        Task.FromResult(ProviderAvailability.Available);
    private CancellationTokenSource? _managedRuntimeStartupCancellation;
    private string _managedRuntimeConfiguration = string.Empty;
    private int _disposeState;

    private AppServices(
        AppDataPaths paths,
        ISettingsStore settingsStore,
        ISecretStore secretStore,
        AppSettings settings,
        EngineProcessClient engine,
        IGlobalHotkeyService hotkeyService,
        IScreenCaptureService screenCaptureService,
        IImageCropper imageCropper,
        HttpClient httpClient)
    {
        Paths = paths;
        SettingsStore = settingsStore;
        SecretStore = secretStore;
        Settings = settings;
        Engine = engine;
        HotkeyService = hotkeyService;
        ScreenCaptureService = screenCaptureService;
        ImageCropper = imageCropper;
        _httpClient = httpClient;

        ManagedModels = new ManagedModelService(paths);
        UpdateService = new GitHubReleaseUpdateService(httpClient);
        PaddleProvider = new PaddleOcrProvider(paths);
        ArgosProvider = new ArgosTranslationProvider(engine);
        BaiduOcrProvider = new BaiduOcrProvider(httpClient, secretStore);
        BaiduTranslationProvider = new BaiduTranslationProvider(httpClient, secretStore);
        GoogleOcrProvider = new GoogleCloudVisionOcrProvider(httpClient, secretStore);
        GoogleTranslationProvider = new GoogleCloudTranslationProvider(httpClient, secretStore);
        CustomTranslationProvider = new ChatCompatibleTranslationProvider(httpClient, secretStore, () => Settings);
        LocalVlmOcrProvider = new ChatCompatibleOcrProvider(
            httpClient,
            secretStore,
            () => Settings,
            CustomTranslationProvider);
        CorrectedLocalOcrProvider = new ChatCompatibleOcrProvider(
            httpClient,
            secretStore,
            () => Settings,
            CustomTranslationProvider,
            PaddleProvider);
        Providers = new ProviderRegistry(
            [PaddleProvider, CorrectedLocalOcrProvider, LocalVlmOcrProvider, BaiduOcrProvider, GoogleOcrProvider],
            [
                ArgosProvider,
                BaiduTranslationProvider,
                GoogleTranslationProvider,
                CustomTranslationProvider
            ]);
        WarmManagedRuntimeIfConfigured();
    }

    public AppDataPaths Paths { get; }
    public ISettingsStore SettingsStore { get; }
    public ISecretStore SecretStore { get; }
    public AppSettings Settings { get; private set; }
    public EngineProcessClient Engine { get; }
    public IGlobalHotkeyService HotkeyService { get; }
    public IScreenCaptureService ScreenCaptureService { get; }
    public IImageCropper ImageCropper { get; }
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
    public ChatCompatibleOcrProvider CorrectedLocalOcrProvider { get; }
    public Task<ProviderAvailability> ManagedRuntimeStartupTask
    {
        get
        {
            lock (_managedRuntimeLock)
            {
                return _managedRuntimeStartupTask;
            }
        }
    }

    public Task ClearDownloadedTranslationModelsAsync(CancellationToken cancellationToken = default) =>
        Engine.CallAsync(
            "delete_models",
            new JsonObject { ["scope"] = "translation" },
            cancellationToken);

    public static async Task<AppServices> CreateAsync(CancellationToken cancellationToken = default)
    {
        var paths = new AppDataPaths();
        var settingsStore = new JsonSettingsStore(paths);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        await settingsStore.SaveAsync(settings, cancellationToken);
        var secretStore = new PlatformSecretStore(paths);
        var engine = new EngineProcessClient(paths);
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PingYi/0.1");
        return new AppServices(
            paths,
            settingsStore,
            secretStore,
            settings,
            engine,
            GlobalHotkeyServiceFactory.Create(),
            ScreenCaptureServiceFactory.Create(),
            new SkiaImageCropper(),
            httpClient);
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        if (!AppSettings.TryParseChatCompletionsEndpoint(settings.CustomTranslationEndpoint, out var endpoint))
        {
            throw new ProviderException(
                "custom_endpoint_invalid",
                "OpenAI 兼容接口地址无效。");
        }
        if (!AppSettings.IsChatCompletionsTransportAllowed(endpoint))
        {
            throw new ProviderException(
                "custom_endpoint_insecure_transport",
                "远程自定义服务必须使用 HTTPS；只有本机回环地址可以使用 HTTP。");
        }

        var normalized = settings.Normalize();
        await _settingsTransitionGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                return;
            }

            var previous = Settings;
            await SettingsStore.SaveAsync(normalized, cancellationToken);
            Settings = normalized;
            if (Settings.ManagedRuntimeEnabled)
            {
                WarmManagedRuntimeIfConfigured();
            }
            else if (previous.ManagedRuntimeEnabled)
            {
                var previousStartup = InvalidateManagedRuntimeStartup();
                CancelAndRelease(previousStartup.Cancellation, previousStartup.Task);
                await ManagedModels.StopAsync(_lifetime.Token);
            }
        }
        finally
        {
            _settingsTransitionGate.Release();
        }
    }

    public async Task<ProviderAvailability> WaitForManagedRuntimeAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                throw new ObjectDisposedException(nameof(AppServices));
            }

            var settings = Settings;
            if (!settings.ManagedRuntimeEnabled)
            {
                return ProviderAvailability.Available;
            }
            if (!ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out var model))
            {
                throw new ProviderException(
                    "managed_runtime_unavailable",
                    "未找到已配置的本机大模型。请在设置中重新选择模型。");
            }

            WarmManagedRuntimeIfConfigured();
            var expectedConfiguration = BuildManagedRuntimeConfiguration(model, settings);
            string configuration;
            Task<ProviderAvailability> startupTask;
            lock (_managedRuntimeLock)
            {
                configuration = _managedRuntimeConfiguration;
                startupTask = _managedRuntimeStartupTask;
            }

            var availability = await startupTask.WaitAsync(cancellationToken);
            if (Volatile.Read(ref _disposeState) != 0)
            {
                throw new ObjectDisposedException(nameof(AppServices));
            }

            lock (_managedRuntimeLock)
            {
                if (!ReferenceEquals(startupTask, _managedRuntimeStartupTask) ||
                    !string.Equals(configuration, _managedRuntimeConfiguration, StringComparison.Ordinal) ||
                    !string.Equals(configuration, expectedConfiguration, StringComparison.Ordinal))
                {
                    continue;
                }
            }

            var currentSettings = Settings;
            if (!currentSettings.ManagedRuntimeEnabled)
            {
                return ProviderAvailability.Available;
            }
            if (!ManagedMultimodalModels.TryGet(currentSettings.ManagedModelPackageId, out var currentModel) ||
                !string.Equals(
                    BuildManagedRuntimeConfiguration(currentModel, currentSettings),
                    expectedConfiguration,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!availability.IsAvailable)
            {
                throw new ProviderException(
                    "managed_runtime_unavailable",
                    availability.Message ?? "本机大模型服务无法启动。");
            }

            return availability;
        }
    }

    private void WarmManagedRuntimeIfConfigured()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        var settings = Settings;
        if (!settings.ManagedRuntimeEnabled ||
            !ManagedMultimodalModels.TryGet(settings.ManagedModelPackageId, out var model))
        {
            return;
        }

        var configuration = BuildManagedRuntimeConfiguration(model, settings);
        CancellationTokenSource? previousCancellation;
        Task<ProviderAvailability>? previousTask;
        lock (_managedRuntimeLock)
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                return;
            }

            if (string.Equals(_managedRuntimeConfiguration, configuration, StringComparison.Ordinal) &&
                !_managedRuntimeStartupTask.IsCompleted)
            {
                return;
            }

            previousCancellation = _managedRuntimeStartupCancellation;
            previousTask = _managedRuntimeStartupTask;
            _managedRuntimeStartupCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _managedRuntimeConfiguration = configuration;
            _managedRuntimeStartupTask = StartManagedRuntimeCoreAsync(
                model,
                settings.ManagedRuntimeBackend,
                _managedRuntimeStartupCancellation.Token);
        }

        CancelAndRelease(previousCancellation, previousTask);
    }

    private static string BuildManagedRuntimeConfiguration(
        ManagedMultimodalModel model,
        AppSettings settings) =>
        $"{model.Id}|{ManagedRuntimeBackends.Normalize(settings.ManagedRuntimeBackend)}";

    private (CancellationTokenSource? Cancellation, Task<ProviderAvailability> Task)
        InvalidateManagedRuntimeStartup()
    {
        lock (_managedRuntimeLock)
        {
            var previous = (_managedRuntimeStartupCancellation, _managedRuntimeStartupTask);
            _managedRuntimeStartupCancellation = null;
            _managedRuntimeConfiguration = string.Empty;
            _managedRuntimeStartupTask = Task.FromResult(ProviderAvailability.Available);
            return previous;
        }
    }

    private static void CancelAndRelease(
        CancellationTokenSource? cancellation,
        Task<ProviderAvailability>? task)
    {
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        if (task is null || task.IsCompleted)
        {
            cancellation.Dispose();
            return;
        }

        _ = task.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            cancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task<ProviderAvailability> StartManagedRuntimeCoreAsync(
        ManagedMultimodalModel model,
        string backend,
        CancellationToken cancellationToken)
    {
        try
        {
            await ManagedModels.EnsureStartedAsync(model, backend, cancellationToken: cancellationToken);
            return ProviderAvailability.Available;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ProviderAvailability(false, "本机大模型启动已取消。");
        }
        catch (Exception exception)
        {
            return new ProviderAvailability(false, exception.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            await _disposeCompletion.Task;
            return;
        }

        try
        {
            // Cancel settings transitions before waiting for their gate. In particular, a
            // transition that is stopping a managed runtime may be queued behind a model
            // download; the shared lifetime token breaks that wait so shutdown cannot
            // deadlock behind an infinite-timeout download.
            _lifetime.Cancel();
            await _settingsTransitionGate.WaitAsync();
            try
            {
                var previousStartup = InvalidateManagedRuntimeStartup();
                CancelAndRelease(previousStartup.Cancellation, previousStartup.Task);
                await previousStartup.Task;
                await HotkeyService.DisposeAsync();
                await ManagedModels.DisposeAsync();
                await Engine.DisposeAsync();
                await PaddleProvider.DisposeAsync();
                _httpClient.Dispose();
            }
            finally
            {
                _settingsTransitionGate.Release();
            }
        }
        finally
        {
            Volatile.Write(ref _disposeState, 2);
            _disposeCompletion.TrySetResult();
        }
    }
}

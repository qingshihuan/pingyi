using PingYi.Core;
using PingYi.Infrastructure;
using System.Text.Json.Nodes;

namespace PingYi.App;

public sealed class AppServices : IAsyncDisposable
{
    private readonly HttpClient _httpClient;

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
        PaddleProvider = new PaddleOcrProvider(paths);
        ArgosProvider = new ArgosTranslationProvider(engine);
        BaiduOcrProvider = new BaiduOcrProvider(httpClient, secretStore);
        BaiduTranslationProvider = new BaiduTranslationProvider(httpClient, secretStore);
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
            [PaddleProvider, CorrectedLocalOcrProvider, LocalVlmOcrProvider, BaiduOcrProvider],
            [
                ArgosProvider,
                BaiduTranslationProvider,
                CustomTranslationProvider
            ]);
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
    public PaddleOcrProvider PaddleProvider { get; }
    public ArgosTranslationProvider ArgosProvider { get; }
    public BaiduOcrProvider BaiduOcrProvider { get; }
    public BaiduTranslationProvider BaiduTranslationProvider { get; }
    public ChatCompatibleTranslationProvider CustomTranslationProvider { get; }
    public ChatCompatibleOcrProvider LocalVlmOcrProvider { get; }
    public ChatCompatibleOcrProvider CorrectedLocalOcrProvider { get; }

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
        Settings = settings.Normalize();
        await SettingsStore.SaveAsync(Settings, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await HotkeyService.DisposeAsync();
        await ManagedModels.DisposeAsync();
        await Engine.DisposeAsync();
        PaddleProvider.Dispose();
        _httpClient.Dispose();
    }
}

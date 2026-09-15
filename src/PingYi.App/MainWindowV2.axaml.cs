using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class MainWindowV2 : Window, IMainWindowShell
{
    private AppServices? _services;
    private CaptureCoordinator? _captureCoordinator;
    private Func<Task>? _openSettings;
    private bool _isRefreshing;
    private readonly PassiveRefreshPolicy _passiveRefresh = new(TimeSpan.FromSeconds(30));

    public MainWindowV2()
    {
        InitializeComponent();
        UiText.Attach(this);
        UiText.LanguageChanged += WindowLanguageChanged;
        Closed += (_, _) => UiText.LanguageChanged -= WindowLanguageChanged;
    }

    public MainWindowV2(
        AppServices services,
        CaptureCoordinator captureCoordinator,
        Func<Task>? openSettings = null) : this()
    {
        _services = services;
        _captureCoordinator = captureCoordinator;
        _openSettings = openSettings;
        Title = UiText.IsEnglish ? "PingYi" : AppEdition.ProductName;
        LoadSettings();
        Opened += async (_, _) => await RefreshDashboardAsync();
        Activated += async (_, _) => await RefreshSettingsFromStoreAsync();
    }

    public void SetGlobalStatus(string message, bool isError)
    {
        TopStatusText.Text = isError ? "需要处理" : "运行正常";
        TopStatusDot.Background = FindBrush(isError ? "DangerBrushV2" : "TealBrush");
        LiveStatusTitleText.Text = isError ? "操作未完成" : "运行正常";
        LiveStatusDetailText.Text = UiText.T(message);
        LiveStatusDot.Background = FindBrush(isError ? "DangerBrushV2" : "TealBrush");
        RecoveryBorder.IsVisible = isError;
        if (isError)
        {
            RecoveryDetailText.Text = UiText.T(message);
        }
    }

    public IReadOnlyList<CaptureDisplay> GetCaptureDisplays() => CaptureDisplay.From(this);

    public void OpenSettings() => _ = OpenSettingsWindowAsync();

    private void LoadSettings()
    {
        if (_services is null)
        {
            return;
        }

        var settings = _services.Settings;
        var ocr = _services.Providers.GetOcrProvider(settings.OcrProviderId).Metadata;
        var translation = _services.Providers.GetTranslationProvider(settings.TranslationProviderId).Metadata;
        var mode = ModeChoice.For(settings);
        ModeSummaryText.Text = mode.Title;
        ModePurposeText.Text = mode.Purpose;
        ModeRequirementsText.Text = mode.Requirements;
        OcrSummaryText.Text = UiText.ProviderName(ocr.Id, ocr.DisplayName);
        var targetLanguage = UiText.LanguageName(settings.TargetLanguage);
        TranslationSummaryText.Text = UiText.ProviderName(translation.Id, translation.DisplayName);
        TargetSummaryText.Text = $"→ {targetLanguage}";
        CaptureHotkeyText.Text = settings.Hotkey.Replace("+", "  ", StringComparison.Ordinal);
    }

    private async Task RefreshSettingsFromStoreAsync()
    {
        if (_services is null || !IsVisible || _isRefreshing || CaptureVisibilityScope.IsActive)
        {
            return;
        }

        LoadSettings();
        if (_passiveRefresh.ShouldRefresh(_services.Settings))
            await RefreshDashboardAsync();
    }

    private async Task RefreshDashboardAsync()
    {
        if (_services is null || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        TopStatusText.Text = "正在检查";
        TopStatusDot.Background = FindBrush("OrangeBrush");
        ModelStatusTitleText.Text = "正在校验基础模型";
        ModelStatusDetailText.Text = "确认无网络环境下仍可执行 OCR 与翻译";
        try
        {
            var settings = _services.Settings;
            // Opening/focusing the UI is not a request to load GiB of model weights.
            var managedOnDemand = RuntimePolicy.UsesManagedRuntime(settings);

            var selectedOcr = _services.Providers.GetOcrProvider(settings.OcrProviderId);
            var selectedTranslation = _services.Providers.GetTranslationProvider(settings.TranslationProviderId);

            var paddleTask = GetAvailabilityAsync(_services.PaddleProvider);
            var argosTask = GetAvailabilityAsync(_services.ArgosProvider);
            var selectedOcrTask = ReferenceEquals(selectedOcr, _services.PaddleProvider)
                ? paddleTask
                : managedOnDemand && (settings.OcrProviderId is "local-vlm-ocr" or "local-vlm-corrected")
                    ? Task.FromResult(ProviderAvailability.Available)
                    : GetAvailabilityAsync(selectedOcr);
            var selectedTranslationTask = ReferenceEquals(selectedTranslation, _services.ArgosProvider)
                ? argosTask
                : managedOnDemand && settings.TranslationProviderId == "custom-chat"
                    ? Task.FromResult(ProviderAvailability.Available)
                    : GetAvailabilityAsync(selectedTranslation);

            await Task.WhenAll(paddleTask, argosTask, selectedOcrTask, selectedTranslationTask);
            var paddle = await paddleTask;
            var argos = await argosTask;
            var ocr = await selectedOcrTask;
            var translation = await selectedTranslationTask;

            var baseModelsReady = paddle.IsAvailable && argos.IsAvailable;
            ModelStatusDot.Background = FindBrush(baseModelsReady ? "SuccessBrushV2" : "OrangeBrush");
            ModelStatusTitleText.Text = baseModelsReady ? "离线基础功能就绪" : "基础模型需要处理";
            ModelStatusDetailText.Text = baseModelsReady
                ? "PaddleOCR 与中英离线翻译均已校验"
                : $"OCR：{DescribeAvailability(paddle)}；翻译：{DescribeAvailability(argos)}";

            var currentModeReady = ocr.IsAvailable && translation.IsAvailable;
            if (baseModelsReady && currentModeReady && managedOnDemand)
            {
                SetGlobalStatus(UiText.IsEnglish
                    ? "Offline models are verified. The local model will be checked and started when you capture."
                    : "离线基础模型已校验；本机大模型将在截图时检查并按需启动。", false);
                TopStatusText.Text = UiText.IsEnglish ? "On demand" : "按需加载";
                LiveStatusTitleText.Text = UiText.IsEnglish ? "Local model: on demand" : "本机大模型按需加载";
            }
            else if (baseModelsReady && currentModeReady)
            {
                SetGlobalStatus(
                    UiText.IsEnglish
                        ? $"{UiText.ProviderName(selectedOcr.Metadata.Id, selectedOcr.Metadata.DisplayName)} · {UiText.ProviderName(selectedTranslation.Metadata.Id, selectedTranslation.Metadata.DisplayName)} is ready. Press {settings.Hotkey} to capture."
                        : $"{selectedOcr.Metadata.DisplayName} · {selectedTranslation.Metadata.DisplayName} 已就绪，按 {settings.Hotkey} 开始截图。",
                    isError: false);
            }
            else
            {
                var detail = !baseModelsReady
                    ? ModelStatusDetailText.Text ?? "离线基础模型不可用。"
                    : $"OCR：{DescribeAvailability(ocr)}；翻译：{DescribeAvailability(translation)}";
                SetGlobalStatus(detail, isError: true);
            }
            _passiveRefresh.RecordRefresh(settings);
        }
        catch (Exception exception)
        {
            SetGlobalStatus(UiText.Error(exception), isError: true);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async void CaptureButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_captureCoordinator is null)
        {
            SetGlobalStatus("截图服务尚未初始化。", isError: true);
            return;
        }

        CaptureButtonV2.IsEnabled = false;
        try
        {
            await _captureCoordinator.StartCaptureAsync(this);
        }
        finally
        {
            CaptureButtonV2.IsEnabled = true;
        }
    }

    private async void ChooseModeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CaptureVisibilityScope.IsActive) return;
        var picker = new ModePickerWindow(_services?.Settings ?? new AppSettings());
        var mode = await DialogPresentation.ShowAsync<string?>(picker, this);
        if (mode is null) return;
        if (mode == ProcessingModes.Custom) { await OpenSettingsWindowAsync(); return; }
        if (_services is null) return;
        try
        {
            ChooseModeButton.IsEnabled = false;
            await _services.SaveSettingsAsync(ProcessingModes.Apply(_services.Settings, mode));
            LoadSettings();
            await RefreshDashboardAsync();
        }
        catch (Exception exception) { SetGlobalStatus(UiText.Error(exception), true); }
        finally { ChooseModeButton.IsEnabled = true; }
    }

    private async void HelpButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CaptureVisibilityScope.IsActive) return;
        await DialogPresentation.ShowAsync(new HelpWindow(_services?.Settings), this);
    }

    private void WindowLanguageChanged(object? sender, EventArgs e)
    {
        Title = UiText.T(AppEdition.ProductName);
        LoadSettings();
    }

    private async void OpenSettingsButton_OnClick(object? sender, RoutedEventArgs e) =>
        await OpenSettingsWindowAsync();

    private async void RepairButton_OnClick(object? sender, RoutedEventArgs e) =>
        await OpenSettingsWindowAsync();

    private async Task OpenSettingsWindowAsync()
    {
        if (CaptureVisibilityScope.IsActive) return;
        if (_openSettings is not null)
        {
            await _openSettings();
            LoadSettings();
            await RefreshDashboardAsync();
            return;
        }

        if (_services is null || _captureCoordinator is null)
        {
            SetGlobalStatus("设置服务尚未初始化。", isError: true);
            return;
        }

        var settingsWindow = new MainWindow(_services, _captureCoordinator);
        await DialogPresentation.ShowAsync(settingsWindow, this);
        LoadSettings();
        await RefreshDashboardAsync();
    }

    private static async Task<ProviderAvailability> GetAvailabilityAsync(IOcrProvider provider)
    {
        try
        {
            return await provider.GetAvailabilityAsync();
        }
        catch (Exception exception)
        {
            return new ProviderAvailability(false, UiText.Error(exception));
        }
    }

    private static async Task<ProviderAvailability> GetAvailabilityAsync(ITranslationProvider provider)
    {
        try
        {
            return await provider.GetAvailabilityAsync();
        }
        catch (Exception exception)
        {
            return new ProviderAvailability(false, UiText.Error(exception));
        }
    }

    private static string DescribeAvailability(ProviderAvailability availability) =>
        availability.IsAvailable ? UiText.T("可用") : UiText.T(availability.Message ?? "不可用");

    private IBrush FindBrush(string key) =>
        TryGetResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush
            ? brush
            : Brushes.Gray;
}

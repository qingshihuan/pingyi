using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using PingYi.Core;

namespace PingYi.App;

public partial class MainWindowV2 : Window, IMainWindowShell
{
    private AppServices? _services;
    private CaptureCoordinator? _captureCoordinator;
    private bool _isRefreshing;

    public MainWindowV2()
    {
        InitializeComponent();
    }

    public MainWindowV2(AppServices services, CaptureCoordinator captureCoordinator) : this()
    {
        _services = services;
        _captureCoordinator = captureCoordinator;
        LoadSettings();
        Opened += async (_, _) => await RefreshDashboardAsync();
        Activated += async (_, _) => await RefreshSettingsFromStoreAsync();
    }

    public void SetGlobalStatus(string message, bool isError)
    {
        TopStatusText.Text = isError ? "需要处理" : "运行正常";
        TopStatusDot.Background = Brush.Parse(isError ? "#B42318" : "#009E8A");
        LiveStatusTitleText.Text = isError ? "操作未完成" : "运行正常";
        LiveStatusDetailText.Text = message;
        LiveStatusDot.Background = Brush.Parse(isError ? "#B42318" : "#009E8A");
        RecoveryBorder.IsVisible = isError;
        if (isError)
        {
            RecoveryDetailText.Text = message;
        }
    }

    private void LoadSettings()
    {
        if (_services is null)
        {
            return;
        }

        var settings = _services.Settings;
        var ocr = _services.Providers.GetOcrProvider(settings.OcrProviderId).Metadata;
        var translation = _services.Providers.GetTranslationProvider(settings.TranslationProviderId).Metadata;
        ModeSummaryText.Text = DescribeMode(settings, ocr, translation);
        OcrSummaryText.Text = ocr.DisplayName;
        var targetLanguage = settings.TargetLanguage == LanguageCatalog.AutoOpposite
            ? "智能中英互换"
            : LanguageCatalog.GetDisplayName(settings.TargetLanguage);
        TranslationSummaryText.Text = $"{translation.DisplayName} · → {targetLanguage}";
        PrivacySummaryText.Text = BuildPrivacyDescription(settings, ocr, translation);
        CaptureHotkeyText.Text = settings.Hotkey.Replace("+", "  ", StringComparison.Ordinal);
    }

    private async Task RefreshSettingsFromStoreAsync()
    {
        if (_services is null || !IsVisible || _isRefreshing)
        {
            return;
        }

        LoadSettings();
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
        TopStatusDot.Background = Brush.Parse("#D94A13");
        ModelStatusTitleText.Text = "正在校验基础模型";
        ModelStatusDetailText.Text = "确认无网络环境下仍可执行 OCR 与翻译";
        try
        {
            var settings = _services.Settings;
            var selectedOcr = _services.Providers.GetOcrProvider(settings.OcrProviderId);
            var selectedTranslation = _services.Providers.GetTranslationProvider(settings.TranslationProviderId);

            var paddleTask = GetAvailabilityAsync(_services.PaddleProvider);
            var argosTask = GetAvailabilityAsync(_services.ArgosProvider);
            var selectedOcrTask = ReferenceEquals(selectedOcr, _services.PaddleProvider)
                ? paddleTask
                : GetAvailabilityAsync(selectedOcr);
            var selectedTranslationTask = ReferenceEquals(selectedTranslation, _services.ArgosProvider)
                ? argosTask
                : GetAvailabilityAsync(selectedTranslation);

            await Task.WhenAll(paddleTask, argosTask, selectedOcrTask, selectedTranslationTask);
            var paddle = await paddleTask;
            var argos = await argosTask;
            var ocr = await selectedOcrTask;
            var translation = await selectedTranslationTask;

            var baseModelsReady = paddle.IsAvailable && argos.IsAvailable;
            ModelStatusDot.Background = Brush.Parse(baseModelsReady ? "#0D7A4B" : "#D94A13");
            ModelStatusTitleText.Text = baseModelsReady ? "离线基础功能就绪" : "基础模型需要处理";
            ModelStatusDetailText.Text = baseModelsReady
                ? "PaddleOCR 与中英离线翻译均已校验"
                : $"OCR：{DescribeAvailability(paddle)}；翻译：{DescribeAvailability(argos)}";

            var currentModeReady = ocr.IsAvailable && translation.IsAvailable;
            if (baseModelsReady && currentModeReady)
            {
                SetGlobalStatus(
                    $"{selectedOcr.Metadata.DisplayName} · {selectedTranslation.Metadata.DisplayName} 已就绪，按 {settings.Hotkey} 开始截图。",
                    isError: false);
            }
            else
            {
                var detail = !baseModelsReady
                    ? ModelStatusDetailText.Text ?? "离线基础模型不可用。"
                    : $"OCR：{DescribeAvailability(ocr)}；翻译：{DescribeAvailability(translation)}";
                SetGlobalStatus(detail, isError: true);
            }
        }
        catch (Exception exception)
        {
            SetGlobalStatus(exception.Message, isError: true);
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

    private async void LocalFirstMenuItem_OnClick(object? sender, RoutedEventArgs e) =>
        await ApplyModeAsync("local-paddle", "local-argos", "本地优先");

    private async void LocalLlmMenuItem_OnClick(object? sender, RoutedEventArgs e) =>
        await ApplyModeAsync("local-paddle", "custom-chat", "本机大模型");

    private async void LocalMultimodalMenuItem_OnClick(object? sender, RoutedEventArgs e) =>
        await ApplyModeAsync("local-vlm-corrected", "custom-chat", "本机多模态");

    private async void CloudModeMenuItem_OnClick(object? sender, RoutedEventArgs e) =>
        await ApplyModeAsync("baidu-ocr", "baidu-translate", "云端增强");

    private async void CustomModeMenuItem_OnClick(object? sender, RoutedEventArgs e) =>
        await OpenSettingsWindowAsync();

    private async Task ApplyModeAsync(string ocrProviderId, string translationProviderId, string modeName)
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            await _services.SaveSettingsAsync(_services.Settings with
            {
                OcrProviderId = ocrProviderId,
                TranslationProviderId = translationProviderId
            });
            LoadSettings();
            SetGlobalStatus($"已切换到“{modeName}”，正在检查可用性。", isError: false);
            await RefreshDashboardAsync();
        }
        catch (Exception exception)
        {
            SetGlobalStatus(exception.Message, isError: true);
        }
    }

    private async void OpenSettingsButton_OnClick(object? sender, RoutedEventArgs e) =>
        await OpenSettingsWindowAsync();

    private async void RepairButton_OnClick(object? sender, RoutedEventArgs e) =>
        await OpenSettingsWindowAsync();

    private async Task OpenSettingsWindowAsync()
    {
        if (_services is null || _captureCoordinator is null)
        {
            SetGlobalStatus("设置服务尚未初始化。", isError: true);
            return;
        }

        var settingsWindow = new MainWindow(_services, _captureCoordinator, settingsMode: true);
        await settingsWindow.ShowDialog(this);
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
            return new ProviderAvailability(false, exception.Message);
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
            return new ProviderAvailability(false, exception.Message);
        }
    }

    private static string DescribeAvailability(ProviderAvailability availability) =>
        availability.IsAvailable ? "可用" : availability.Message ?? "不可用";

    private static string DescribeMode(
        AppSettings settings,
        ProviderMetadata ocr,
        ProviderMetadata translation)
    {
        if (settings.TranslationProviderId == "custom-chat")
        {
            return Uri.TryCreate(settings.CustomTranslationEndpoint, UriKind.Absolute, out var endpoint) &&
                   endpoint.IsLoopback
                ? "本机大模型"
                : "自定义服务";
        }

        if (settings.OcrProviderId == "baidu-ocr" || settings.TranslationProviderId == "baidu-translate")
        {
            return "云端增强";
        }

        return ocr.Location == ProviderExecutionLocation.Local &&
               translation.Location == ProviderExecutionLocation.Local
            ? "本地优先"
            : "自定义组合";
    }

    private static string BuildPrivacyDescription(
        AppSettings settings,
        ProviderMetadata ocr,
        ProviderMetadata translation)
    {
        var localModelEndpoint = Uri.TryCreate(
            settings.CustomTranslationEndpoint,
            UriKind.Absolute,
            out var endpoint) && endpoint.IsLoopback;
        var usesLocalVision = (ocr.Id is "local-vlm-ocr" or "local-vlm-corrected") && localModelEndpoint;
        var usesSameLocalTranslation = translation.Id == "custom-chat" && localModelEndpoint;
        if (usesLocalVision && (translation.Location == ProviderExecutionLocation.Local || usesSameLocalTranslation))
        {
            return "图片与文字只发送到本机大模型服务，不会离开设备。";
        }

        if (ocr.Location == ProviderExecutionLocation.Local &&
            translation.Location == ProviderExecutionLocation.Local)
        {
            return "全程在本机处理；截图、原文和译文不会离开设备。";
        }

        if (ocr.UploadsImage)
        {
            return $"所选图片发送给 {ocr.DisplayName}；识别文字发送给 {translation.DisplayName}。";
        }

        return $"图片在本地识别；只有识别文字会发送给 {translation.DisplayName}。";
    }
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class MainWindow : Window, IMainWindowShell
{
    private AppServices? _services;
    private CaptureCoordinator? _captureCoordinator;
    private Func<Task>? _openSettings;
    private bool _isRefreshing;
    private ModePickerWindow? _modePicker;
    private HelpAboutWindow? _helpWindow;
    private readonly PassiveRefreshPolicy _passiveRefresh = new(TimeSpan.FromSeconds(30));

    public MainWindow()
    {
        InitializeComponent();
        InitializeSmartCaptureUi();
        UiText.Attach(this);
        UpdateProductTitle();
        UiText.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => UiText.LanguageChanged -= OnLanguageChanged;
    }
    public MainWindow(AppServices services, CaptureCoordinator captureCoordinator, Func<Task>? openSettings = null) : this()
    {
        _services = services; _captureCoordinator = captureCoordinator; _openSettings = openSettings;
        LoadSettings();
        Opened += async (_, _) => { await ShowInitialSetupAsync(); await RefreshDashboardAsync(); };
        Activated += async (_, _) => await RefreshSettingsFromStoreAsync();
    }
    public void SetGlobalStatus(string message, bool isError)
    {
        if (!isError && _services?.HotkeyRegistrationError is { } error) { message = LinuxDesktopUi.DescribeError(error); isError = true; }
        TopStatusText.Text = isError ? "需要处理" : "运行正常";
        TopStatusDot.Background = FindBrush(isError ? "DangerBrushV2" : "TealBrush");
        LiveStatusTitleText.Text = isError ? "操作未完成" : "运行正常";
        LiveStatusDetailText.Text = UiText.T(message);
        LiveStatusDot.Background = FindBrush(isError ? "DangerBrushV2" : "TealBrush");
        RecoveryBorder.IsVisible = isError;
        if (isError) RecoveryDetailText.Text = UiText.T(message);
    }
    public IReadOnlyList<CaptureDisplay> GetCaptureDisplays() => CaptureDisplay.From(this);
    public void OpenSettings() => _ = OpenSettingsWindowAsync();
    private void LoadSettings()
    {
        RefreshSmartCaptureUi();
        if (_services is null) return;
        var settings = _services.Settings;
        var ocr = _services.Providers.GetOcrProvider(settings.OcrProviderId).Metadata;
        var translation = _services.Providers.GetTranslationProvider(settings.TranslationProviderId).Metadata;
        ModeSummaryText.Text = ModePickerWindow.NameFor(ProcessingModes.Match(settings).Id);
        OcrSummaryText.Text = UiText.ProviderName(ocr.Id, ocr.DisplayName);
        TranslationSummaryText.Text = $"{UiText.ProviderName(translation.Id, translation.DisplayName)} · → {UiText.LanguageName(settings.TargetLanguage)}";
        CaptureHotkeyText.Text = LinuxDesktopUi.ShortcutLabel(settings.Hotkey);
    }
    private async Task RefreshSettingsFromStoreAsync()
    {
        if (_services is null || !IsVisible || _isRefreshing) return;
        LoadSettings();
        if (_passiveRefresh.ShouldRefresh(_services.Settings)) await RefreshDashboardAsync();
    }
    private async Task RefreshDashboardAsync()
    {
        if (_services is null || _isRefreshing || _services.IsInitialSetupActive) return;
        _isRefreshing = true;
        TopStatusText.Text = "正在检查"; TopStatusDot.Background = FindBrush("OrangeBrush");
        ModelStatusTitleText.Text = CaptureUiText.Pick("正在检查轻量模型", "Checking lightweight models");
        ModelStatusDetailText.Text = CaptureUiText.Pick("本地意图探测及离线中英兜底", "Local content detection and offline Chinese/English fallback");
        try
        {
            var settings = _services.Settings;
            var managedOnDemand = RuntimePolicy.UsesManagedRuntime(settings);
            var selectedOcr = _services.Providers.GetOcrProvider(settings.OcrProviderId);
            var selectedTranslation = _services.Providers.GetTranslationProvider(settings.TranslationProviderId);
            var paddleTask = GetAvailabilityAsync(_services.PaddleProvider);
            var argosTask = GetAvailabilityAsync(_services.ArgosProvider);
            var ocrTask = ReferenceEquals(selectedOcr, _services.PaddleProvider) ? paddleTask
                : managedOnDemand && settings.OcrProviderId == "local-vlm-ocr" ? Task.FromResult(ProviderAvailability.Available) : GetAvailabilityAsync(selectedOcr);
            var translationTask = ReferenceEquals(selectedTranslation, _services.ArgosProvider) ? argosTask
                : managedOnDemand && settings.TranslationProviderId == "custom-chat" ? Task.FromResult(ProviderAvailability.Available) : GetAvailabilityAsync(selectedTranslation);
            await Task.WhenAll(paddleTask, argosTask, ocrTask, translationTask);
            var paddle = await paddleTask; var argos = await argosTask; var ocr = await ocrTask; var translation = await translationTask;
            var lightweightReady = paddle.IsAvailable && argos.IsAvailable;
            ModelStatusDot.Background = FindBrush(lightweightReady ? "SuccessBrushV2" : "OrangeBrush");
            ModelStatusTitleText.Text = lightweightReady ? CaptureUiText.Pick("轻量模式可用", "Lightweight mode available") : CaptureUiText.Pick("轻量模型需要处理", "Lightweight models need attention");
            ModelStatusDetailText.Text = lightweightReady ? CaptureUiText.Pick("PaddleOCR 与 Argos 作为本地探测和离线兜底", "PaddleOCR and Argos provide local detection and offline fallback")
                : $"OCR：{DescribeAvailability(paddle)}；翻译：{DescribeAvailability(argos)}";
            OcrHealthText.Text = managedOnDemand && settings.OcrProviderId == "local-vlm-ocr" ? UiText.Get("String.OnDemand") : DescribeAvailability(ocr);
            TranslationHealthText.Text = managedOnDemand && settings.TranslationProviderId == "custom-chat" ? UiText.Get("String.OnDemand") : DescribeAvailability(translation);
            if (ocr.IsAvailable && translation.IsAvailable && managedOnDemand)
            {
                SetGlobalStatus(CaptureUiText.Pick("本机大模型将在需要时检查并按需启动。", "The local model will be checked and started when needed."), false);
                TopStatusText.Text = UiText.IsEnglish ? "On demand" : "按需加载";
                LiveStatusTitleText.Text = UiText.IsEnglish ? "Local model: on demand" : "本机大模型按需加载";
            }
            else if (ocr.IsAvailable && translation.IsAvailable)
                SetGlobalStatus(CaptureUiText.Pick("已就绪，点击“一键识别”或选择手动任务。", "Ready. Use Smart capture or choose a manual task."), false);
            else SetGlobalStatus($"{UiText.T("文字识别")}: {DescribeAvailability(ocr)}; {UiText.T("翻译方式")}: {DescribeAvailability(translation)}", true);
            _passiveRefresh.RecordRefresh(settings);
        }
        catch (Exception error) { SetGlobalStatus(UiText.Error(error), true); }
        finally { _isRefreshing = false; }
    }
    private async void CaptureButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_captureCoordinator is null) { SetGlobalStatus("截图服务尚未初始化。", true); return; }
        CaptureButtonV2.IsEnabled = false;
        try { await _captureCoordinator.StartCaptureAsync(this); }
        finally { CaptureButtonV2.IsEnabled = true; }
    }
    private void OnLanguageChanged(object? sender, EventArgs e) { UpdateProductTitle(); LoadSettings(); }
    private void UpdateProductTitle() => Title = UiText.IsEnglish ? "Screen Insight Complete" : AppEdition.ProductName;
    private async void ChooseMode_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_modePicker is not null) { _modePicker.Activate(); return; }
        var picker = new ModePickerWindow(_services?.Settings ?? new AppSettings());
        _modePicker = picker;
        try
        {
            var selected = await picker.SelectAsync(this);
            if (selected is null) return;
            if (selected == "custom") { await OpenSettingsWindowAsync(); return; }
            if (_services is null) return;
            if (selected == "basic" && (!ProcessingModes.IsLocalEndpoint(_services.Settings) || !_services.Settings.InitialSetupCompleted))
            { await ShowInitialSetupAsync(force: true); return; }
            var settings = ProcessingModes.Apply(_services.Settings, selected);
            var supported = _services.Providers.GetTranslationProvider(settings.TranslationProviderId).Metadata.SupportedLanguages;
            if (settings.TargetLanguage != LanguageCatalog.AutoOpposite && !supported.Contains(settings.TargetLanguage, StringComparer.OrdinalIgnoreCase))
                settings = settings with { TargetLanguage = LanguageCatalog.AutoOpposite };
            await _services.SaveSettingsAsync(settings);
            LoadSettings(); await RefreshDashboardAsync();
        }
        catch (Exception error) { SetGlobalStatus(UiText.Error(error), true); }
        finally { _modePicker = null; }
    }
    private void OpenHelp_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_helpWindow is not null) { _helpWindow.Show(); _helpWindow.Activate(); return; }
        _helpWindow = new HelpAboutWindow(() => _services?.Settings ?? new AppSettings());
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Show(this);
    }
    private async void OpenSettingsButton_OnClick(object? sender, RoutedEventArgs e) => await OpenSettingsWindowAsync();
    private async void RepairButton_OnClick(object? sender, RoutedEventArgs e) => await OpenSettingsWindowAsync();
    private async Task OpenSettingsWindowAsync()
    {
        if (_openSettings is not null) { await _openSettings(); LoadSettings(); await RefreshDashboardAsync(); return; }
        if (_services is null || _captureCoordinator is null) { SetGlobalStatus("设置服务尚未初始化。", true); return; }
        new SettingsWindow(_services).Show(this);
        LoadSettings(); await RefreshDashboardAsync();
    }
    private static async Task<ProviderAvailability> GetAvailabilityAsync(IOcrProvider provider)
    {
        try { return await provider.GetAvailabilityAsync(); }
        catch (Exception error) { return new ProviderAvailability(false, UiText.Error(error)); }
    }
    private static async Task<ProviderAvailability> GetAvailabilityAsync(ITranslationProvider provider)
    {
        try { return await provider.GetAvailabilityAsync(); }
        catch (Exception error) { return new ProviderAvailability(false, UiText.Error(error)); }
    }
    private static string DescribeAvailability(ProviderAvailability availability) => availability.IsAvailable ? UiText.T("可用") : UiText.T(availability.Message ?? "不可用");
    private IBrush FindBrush(string key) => TryGetResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush ? brush : Brushes.Gray;
}

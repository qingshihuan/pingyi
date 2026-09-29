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
        InitializeExitControl();
        InitializeModeStatusUi();
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
    private async Task RefreshDashboardAsync(bool force = false)
    {
        if (_services is null || _isRefreshing || _statusClosed || _services.IsInitialSetupActive) return;
        _isRefreshing = true;
        TopStatusText.Text = ModeStatusText.Pick("正在检查", "Checking");
        TopStatusDot.Background = FindBrush("TertiaryTextBrush");
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var snapshot = await _services.ReadRuntimeStatusAsync(_statusLifetime.Token, force);
                if (_statusClosed) return;
                if (snapshot.Settings != _services.Settings) continue;
                RenderRuntimeStatus(snapshot);
                RenderPipelineStatus(snapshot);
                _passiveRefresh.RecordRefresh(snapshot.Settings);
                return;
            }
        }
        catch (OperationCanceledException) when (_statusClosed || _services.IsShuttingDown) { }
        catch (Exception) { if (!_statusClosed) SetGlobalStatus(ModeStatusText.Pick("状态检查未完成，请重试。", "Status check did not complete; retry."), true); }
        finally { _isRefreshing = false; }
    }
    private async void CaptureButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_captureCoordinator is null) { SetGlobalStatus("截图服务尚未初始化。", true); return; }
        CaptureButtonV2.IsEnabled = false;
        try { await _captureCoordinator.StartCaptureAsync(this); }
        finally { CaptureButtonV2.IsEnabled = true; }
    }
    private void OnLanguageChanged(object? sender, EventArgs e) { UpdateProductTitle(); LoadSettings(); RefreshModeStatusLanguage(); }
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

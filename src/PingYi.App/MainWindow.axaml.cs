using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class MainWindow : Window, IMainWindowShell
{
    private AppServices? _services;
    private CaptureCoordinator? _captureCoordinator;
    private Func<Task>? _openSettings;
    private HelpAboutWindow? _helpWindow;

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
        InitializeStatusUpdates(() => services.Settings, services.ReadRuntimeStatusAsync,
            () => services.IsInitialSetupActive || services.IsShuttingDown);
        EventHandler changed = (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshDashboardAsync(force: true));
        services.RuntimeStatusChanged += changed;
        Closed += (_, _) => services.RuntimeStatusChanged -= changed;
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
        CaptureHotkeyText.Text = LinuxDesktopUi.ShortcutLabel(settings.Hotkey);
        try
        {
            var ocr = _services.TaskOcr(settings).Metadata;
            var translation = _services.TaskTranslator(settings).Metadata;
            ModeSummaryText.Text = CaptureUiText.Pick("当前多模态模型", "Current multimodal model");
            OcrSummaryText.Text = ocr.DisplayName;
            TranslationSummaryText.Text = $"{translation.DisplayName} · → {UiText.LanguageName(settings.TargetLanguage)}";
        }
        catch (ProviderException error)
        {
            ModeSummaryText.Text = ModeStatusText.Pick("请选择当前模型", "Select a current model");
            OcrSummaryText.Text = TranslationSummaryText.Text = ModeStatusText.Pick("请在设置中验证并选择多模态模型", "Verify and select a multimodal model in Settings");
            SetGlobalStatus(UiText.Error(error), true);
        }
    }
    private async Task RefreshSettingsFromStoreAsync()
    {
        if (_services is null || !IsVisible) return;
        LoadSettings();
        await RefreshDashboardAsync();
    }
    internal Task RefreshDashboardAsync(bool force = false) => _statusUpdater?.RequestAsync(force) ?? Task.CompletedTask;
    internal void InitializeStatusUpdates(Func<AppSettings> settings,
        Func<CancellationToken, bool, Task<RuntimeStatusSnapshot>> read, Func<bool> paused)
    {
        _statusUpdater?.Dispose();
        _statusUpdater = new PassiveStatusUpdater(
            () => IsVisible && WindowState != WindowState.Minimized && !_statusClosed && !paused(),
            (token, force) => RefreshDashboardCoreAsync(settings, read, token, force));
    }
    private async Task RefreshDashboardCoreAsync(Func<AppSettings> settings,
        Func<CancellationToken, bool, Task<RuntimeStatusSnapshot>> read, CancellationToken token, bool force)
    {
        if (force || _runtimeStatusSnapshot.CheckedAt is null)
        {
            TopStatusText.Text = ModeStatusText.Pick("正在检查", "Checking");
            TopStatusDot.Background = FindBrush("TertiaryTextBrush");
        }
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var snapshot = await read(token, force);
                if (_statusClosed) return;
                if (snapshot.Settings != settings()) continue;
                if (force || !_runtimeStatusSnapshot.SameState(snapshot) || _runtimeStatusSnapshot.CheckedAt is null)
                {
                    LoadSettings(); RenderRuntimeStatus(snapshot); RenderPipelineStatus(snapshot);
                }
                return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || _services?.IsShuttingDown == true) { }
        catch (Exception) { if (!_statusClosed) SetGlobalStatus(ModeStatusText.Pick("状态检查未完成，请重试。", "Status check did not complete; retry."), true); }
    }
    private async void CaptureButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var timing = new CaptureTiming();
        timing.Mark("capture_button_received");
        if (_captureCoordinator is null) { SetGlobalStatus("截图服务尚未初始化。", true); return; }
        CaptureButtonV2.IsEnabled = false;
        try { await _captureCoordinator.StartCaptureAsync(this, timing: timing); }
        finally { CaptureButtonV2.IsEnabled = true; }
    }
    private void OnLanguageChanged(object? sender, EventArgs e) { UpdateProductTitle(); LoadSettings(); RefreshModeStatusLanguage(); }
    private void UpdateProductTitle() => Title = UiText.IsEnglish ? "Screen Insight Complete" : AppEdition.ProductName;
    private async void ChooseMode_OnClick(object? sender, RoutedEventArgs e) => await OpenSettingsWindowAsync();
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

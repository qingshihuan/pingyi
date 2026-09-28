using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

internal enum InitialSetupChoice { None, Ready, ConfigureExisting }

internal sealed class FirstRunSetupWindow : Window
{
    private readonly AppServices _services;
    private readonly ComboBox _models = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly ComboBox _backends = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Height = 5, IsVisible = false };
    private readonly Button _install = new();
    private readonly Button _lite = new();
    private readonly Button _existing = new();
    private readonly Button _cancel = new();
    private CancellationTokenSource? _operation;
    private bool _closeRequested;

    internal FirstRunSetupWindow(AppServices services)
    {
        _services = services;
        Title = CaptureUiText.Pick("开始使用 · 选择处理模式", "Get started · choose a processing mode");
        Width = 600; Height = 590; MinWidth = 440; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("workspace");
        Styles.Add(new StyleInclude(new Uri("avares://PingYi.App/")) { Source = new Uri("avares://PingYi.App/Styles/Workspace.axaml") });
        _models.ItemsSource = ManagedMultimodalModels.All;
        _models.SelectedItem = ManagedMultimodalModels.Recommended;
        _backends.ItemsSource = ManagedRuntimeBackends.All;
        _backends.SelectedItem = ManagedRuntimeBackends.Auto;
        _models.SelectionChanged += (_, _) => UpdateDetails();
        _install.Content = CaptureUiText.Pick("一键下载并配置基础模式", "Download and configure Basic");
        _lite.Content = CaptureUiText.Pick("暂不下载，使用轻量模式", "Skip download · use Lightweight");
        _existing.Content = CaptureUiText.Pick("我已有本机模型服务", "Connect an existing local service");
        _cancel.Content = CaptureUiText.Pick("取消下载／启动", "Cancel download / startup");
        _install.Classes.Add("primary"); _lite.Classes.Add("secondary"); _existing.Classes.Add("secondary"); _cancel.Classes.Add("secondary");
        _cancel.IsVisible = false;
        _install.Click += async (_, _) => await InstallAsync();
        _lite.Click += async (_, _) => await UseLiteAsync();
        _existing.Click += async (_, _) =>
        {
            try
            {
                await _services.SaveSettingsAsync(_services.Settings with { InitialSetupCompleted = true });
                Close(InitialSetupChoice.ConfigureExisting);
            }
            catch (Exception error) { _status.Text = UiText.Error(error); }
        };
        _cancel.Click += (_, _) => _operation?.Cancel();
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(28), Spacing = 14,
                Children =
                {
                    new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = CaptureUiText.Pick("推荐基础模式：本机视觉模型直接识别文字，再由本机模型翻译。截图后自动判断翻译、图片描述或二维码解析，始终可手动改选。", "Basic uses direct local vision OCR and local model translation. Smart capture selects translation, image description or QR decoding; manual choices remain available."), TextWrapping = TextWrapping.Wrap },
                    _models, _backends, _details,
                    new TextBlock { Text = CaptureUiText.Pick("只有点击下载才会联网，从魔搭获取模型并校验文件；模型不在安装包内。需要足够磁盘和内存，速度取决于设备。取消后保留已下载部分以便续传。", "Only clicking Download connects to ModelScope. Weights are verified and are not bundled. Allow sufficient disk space and memory; performance depends on your device. Cancelling preserves resumable downloads."), TextWrapping = TextWrapping.Wrap },
                    _install, _progress, _status, _cancel, _existing, _lite
                }
            }
        };
        UpdateDetails();
        if (!_services.ManagedModels.HasBundledRuntime)
        {
            _install.IsEnabled = false;
            _status.Text = CaptureUiText.Pick("源码构建或当前目录缺少 llama.cpp 运行时。请使用含运行时的完整安装包，或选择已有服务／轻量模式。", "This source build or directory lacks llama.cpp. Use a complete runtime bundle, an existing service, or Lightweight mode.");
        }
        Closing += (_, e) =>
        {
            if (_operation is null) return;
            e.Cancel = true; _closeRequested = true; _operation.Cancel();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (_operation is not null) _operation.Cancel(); else Close(InitialSetupChoice.None);
        };
    }
    private void UpdateDetails()
    {
        if (_models.SelectedItem is not ManagedMultimodalModel model) return;
        _details.Text = $"{model.LocalizedDisplayName}\n{model.TotalSize / 1073741824d:0.00} GiB · {model.License}\n{model.LocalizedHardwareHint}";
    }
    private void SetBusy(bool busy)
    {
        _install.IsEnabled = _lite.IsEnabled = _existing.IsEnabled = _models.IsEnabled = _backends.IsEnabled = !busy;
        _cancel.IsVisible = _progress.IsVisible = busy;
    }
    private async Task InstallAsync()
    {
        if (_operation is not null || _models.SelectedItem is not ManagedMultimodalModel model) return;
        _operation = new CancellationTokenSource();
        SetBusy(true);
        _status.Text = CaptureUiText.Pick("正在下载、校验和启动，随后验证图像识别与翻译…", "Downloading, verifying and starting; image OCR and translation will then be checked…");
        var ready = false;
        try
        {
            var progress = new Progress<ManagedModelProgress>(value =>
            {
                if (_operation is null || !IsVisible) return;
                _progress.IsIndeterminate = value.IsIndeterminate;
                _progress.Value = value.Percentage;
                _status.Text = UiText.T(value.Message);
            });
            await _services.ConfigureInitialModelAsync(model,
                (_backends.SelectedItem as ManagedRuntimeBackend)?.Id ?? "auto", progress, _operation.Token);
            ready = true;
        }
        catch (OperationCanceledException) { _status.Text = CaptureUiText.Pick("已取消，未完成首次配置；可以重试或使用轻量模式。", "Cancelled. Setup was not completed; retry or use Lightweight."); }
        catch (Exception error) { _status.Text = UiText.Error(error); }
        finally { _operation.Dispose(); _operation = null; SetBusy(false); }
        if (ready) Close(InitialSetupChoice.Ready);
        else if (_closeRequested) Close(InitialSetupChoice.None);
    }
    private async Task UseLiteAsync()
    {
        if (_operation is not null) return;
        try
        {
            await _services.SaveSettingsAsync(ProcessingModes.Apply(_services.Settings, "lite") with { InitialSetupCompleted = true });
            Close(InitialSetupChoice.Ready);
        }
        catch (Exception error) { _status.Text = UiText.Error(error); }
    }
}

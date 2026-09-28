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
    private readonly Func<ManagedMultimodalModel, string, IProgress<ManagedModelProgress>, CancellationToken, Task> _configure;
    private readonly Func<InitialSetupChoice, CancellationToken, Task> _saveChoice;
    private readonly bool _hasRuntime;
    private readonly ComboBox _models = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly ComboBox _backends = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Height = 5, IsVisible = false };
    internal Button DownloadButton { get; } = new() { Name = "DownloadBasicButton" };
    internal Button LightweightButton { get; } = new() { Name = "UseLightweightButton" };
    internal Button ExistingButton { get; } = new() { Name = "UseExistingModelButton" };
    internal Button CancelButton { get; } = new() { Name = "CancelModelSetupButton" };
    internal Task OperationTask { get; private set; } = Task.CompletedTask;
    private CancellationTokenSource? _operation;
    private bool _closeRequested;

    internal FirstRunSetupWindow(AppServices services) : this(services.ManagedModels.HasBundledRuntime,
        services.ConfigureInitialModelAsync, async (choice, token) =>
        {
            var settings = choice == InitialSetupChoice.Ready
                ? ProcessingModes.Apply(services.Settings, "lite") : services.Settings;
            await services.SaveSettingsAsync(settings with { InitialSetupCompleted = true }, token);
        }) { }

    // Separate UI from download/persistence callbacks so confirmation and cancellation can
    // be tested with no network, model files, credentials or user settings.
    internal FirstRunSetupWindow(bool hasRuntime,
        Func<ManagedMultimodalModel, string, IProgress<ManagedModelProgress>, CancellationToken, Task> configure,
        Func<InitialSetupChoice, CancellationToken, Task> saveChoice)
    {
        _hasRuntime = hasRuntime; _configure = configure; _saveChoice = saveChoice;
        Title = CaptureUiText.Pick("开始使用 · 选择处理模式", "Get started · choose a processing mode");
        Width = 600; Height = 620; MinWidth = 440; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("workspace");
        Styles.Add(new StyleInclude(new Uri("avares://PingYi.App/")) { Source = new Uri("avares://PingYi.App/Styles/Workspace.axaml") });
        _models.ItemsSource = ManagedMultimodalModels.All;
        _models.SelectedItem = ManagedMultimodalModels.Recommended;
        _backends.ItemsSource = ManagedRuntimeBackends.All;
        _backends.SelectedItem = ManagedRuntimeBackends.Auto;
        _models.SelectionChanged += (_, _) => UpdateDetails();
        DownloadButton.Content = CaptureUiText.Pick("一键下载并配置基础模式", "Download and configure Basic");
        LightweightButton.Content = CaptureUiText.Pick("暂不下载，使用轻量模式", "Skip download · use Lightweight");
        ExistingButton.Content = CaptureUiText.Pick("我已有本机模型服务", "Connect an existing local service");
        CancelButton.Content = CaptureUiText.Pick("取消下载／启动", "Cancel download / startup");
        DownloadButton.Classes.Add("primary");
        foreach (var button in new[] { LightweightButton, ExistingButton, CancelButton }) button.Classes.Add("secondary");
        foreach (var button in new[] { DownloadButton, LightweightButton, ExistingButton, CancelButton })
        {
            AutomationProperties.SetAutomationId(button, button.Name);
            AutomationProperties.SetName(button, button.Content?.ToString() ?? "");
        }
        CancelButton.IsVisible = false;
        DownloadButton.Click += async (_, _) => { OperationTask = InstallAsync(); await OperationTask; };
        LightweightButton.Click += async (_, _) => { OperationTask = ChooseAsync(InitialSetupChoice.Ready); await OperationTask; };
        ExistingButton.Click += async (_, _) => { OperationTask = ChooseAsync(InitialSetupChoice.ConfigureExisting); await OperationTask; };
        CancelButton.Click += (_, _) => _operation?.Cancel();
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = CaptureUiText.Pick("基础模式：本机视觉 OCR ＋ 本机模型翻译。一次截图自动判断任务，也可手动选择翻译、描述或二维码。", "Basic: local vision OCR + local model translation. Smart capture chooses a task; you can always choose translation, description or QR manually."), TextWrapping = TextWrapping.Wrap },
                    _models, _backends, _details,
                    new TextBlock { Text = CaptureUiText.Pick("点击后从魔搭下载并校验模型。模型不在安装包内，需要足够磁盘与内存；取消后保留已下载部分以便续传。不上传你的截图。", "Clicking Download fetches and verifies weights from ModelScope. They are not bundled; allow sufficient disk and memory. Cancelling preserves resumable downloads. Your screenshots are not uploaded."), TextWrapping = TextWrapping.Wrap },
                    DownloadButton, _progress, _status, CancelButton, ExistingButton, LightweightButton
                }
            }
        };
        UpdateDetails();
        if (!hasRuntime)
        {
            DownloadButton.IsEnabled = false;
            _status.Text = CaptureUiText.Pick("当前目录缺少 llama.cpp 运行时。请使用完整安装包，或选择已有本机服务／轻量模式。", "This directory lacks llama.cpp. Use a complete runtime bundle, an existing local service, or Lightweight.");
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
        if (_models.SelectedItem is ManagedMultimodalModel model)
            _details.Text = $"{model.LocalizedDisplayName}\n{model.TotalSize / 1073741824d:0.00} GiB · {model.License}\n{model.LocalizedHardwareHint}";
    }
    private void SetBusy(bool busy)
    {
        DownloadButton.IsEnabled = !busy && _hasRuntime;
        LightweightButton.IsEnabled = ExistingButton.IsEnabled = _models.IsEnabled = _backends.IsEnabled = !busy;
        CancelButton.IsVisible = _progress.IsVisible = busy;
    }
    private async Task InstallAsync()
    {
        if (_operation is not null || !_hasRuntime || _models.SelectedItem is not ManagedMultimodalModel model) return;
        _operation = new CancellationTokenSource();
        SetBusy(true);
        _status.Text = CaptureUiText.Pick("正在下载、校验、启动并测试 OCR 与翻译…", "Downloading, verifying, starting and testing OCR and translation…");
        var ready = false;
        try
        {
            var progress = new Progress<ManagedModelProgress>(value =>
            {
                if (_operation is null || !IsVisible) return;
                _progress.IsIndeterminate = value.IsIndeterminate; _progress.Value = value.Percentage;
                _status.Text = UiText.T(value.Message);
            });
            await _configure(model, (_backends.SelectedItem as ManagedRuntimeBackend)?.Id ?? "auto", progress, _operation.Token);
            ready = true;
        }
        catch (OperationCanceledException) { _status.Text = CaptureUiText.Pick("已取消，未完成配置；可以重试或使用轻量模式。", "Cancelled. Setup was not completed; retry or use Lightweight."); }
        catch (Exception error) { _status.Text = UiText.Error(error); }
        finally { _operation.Dispose(); _operation = null; SetBusy(false); }
        if (ready) Close(InitialSetupChoice.Ready);
        else if (_closeRequested) Close(InitialSetupChoice.None);
    }
    private async Task ChooseAsync(InitialSetupChoice choice)
    {
        if (_operation is not null) return;
        _operation = new CancellationTokenSource();
        SetBusy(true);
        var saved = false;
        try { await _saveChoice(choice, _operation.Token); saved = true; }
        catch (OperationCanceledException) { _status.Text = CaptureUiText.NotSent; }
        catch (Exception error) { _status.Text = UiText.Error(error); }
        finally { _operation.Dispose(); _operation = null; SetBusy(false); }
        if (saved) Close(choice);
        else if (_closeRequested) Close(InitialSetupChoice.None);
    }
}

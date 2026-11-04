using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

internal enum InitialSetupChoice { None, Ready, ConfigureExisting }

internal sealed class FirstRunSetupWindow : Window
{
    private readonly Func<ManagedMultimodalModel, string, IProgress<ManagedModelProgress>, CancellationToken, Task> _configure;
    private readonly Func<bool, string, IProgress<ManagedModelProgress>, CancellationToken, Task> _configureAutomatically;
    private readonly Func<InitialSetupChoice, CancellationToken, Task> _saveChoice;
    private RuntimeSetupPanel? _runtimeHardware;
    private readonly StackPanel _runtimeHost = new() { Spacing = 8 };
    private readonly ComboBox _models = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly ComboBox _backends = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Height = 5, IsVisible = false };
    internal Button AutomaticButton { get; } = new() { Name = "AutomaticLocalSetupButton" };
    internal Expander ManualOptions { get; } = new()
    {
        Name = "ManualLocalSetupOptions", IsExpanded = false,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
    };
    internal Button DownloadButton { get; } = new() { Name = "DownloadBasicButton" };
    internal Button ExistingButton { get; } = new() { Name = "UseExistingModelButton" };
    internal Button ApiButton { get; } = new() { Name = "ConfigureApiButton" };
    internal Button CancelButton { get; } = new() { Name = "CancelModelSetupButton" };
    internal Task OperationTask { get; private set; } = Task.CompletedTask;
    private CancellationTokenSource? _operation;
    private bool _closeRequested;

    internal FirstRunSetupWindow(AppServices services) : this(services.ManagedModels.HasBundledRuntime,
        services.ConfigureInitialModelAsync, services.ConfigureLocalModelAutomaticallyAsync, async (choice, token) =>
        {
            var settings = services.Settings;
            await services.SaveSettingsAsync(settings with { InitialSetupCompleted = true }, token);
        })
    {
        _backends.SelectedItem = ManagedRuntimeBackends.Get(services.Settings.ManagedRuntimeBackend);
        _runtimeHardware = new RuntimeSetupPanel(services.ManagedModels.Runtimes, _backends, services.Settings,
            () => services.ManagedModels.CurrentRuntimeDescription, services);
        _runtimeHost.Children.Add(_runtimeHardware);
        _runtimeHardware.ActivityChanged += busy =>
        {
            AutomaticButton.IsEnabled = DownloadButton.IsEnabled = !busy && _operation is null;
            ExistingButton.IsEnabled = ApiButton.IsEnabled = !busy;
        };
        _configure = (model, backend, progress, token) => services.ConfigureInitialModelAsync(model, backend,
            _runtimeHardware.SelectedDevice, _runtimeHardware.AllowMirrors, _runtimeHardware.MirrorPrefixes, progress, token);
        ManualOptions.PropertyChanged += async (_, e) =>
        {
            if (e.Property == Expander.IsExpandedProperty && ManualOptions.IsExpanded)
                await _runtimeHardware.RefreshAsync();
        };
        Closed += (_, _) => _runtimeHardware.Cancel();
    }

    internal FirstRunSetupWindow(bool hasRuntime,
        Func<ManagedMultimodalModel, string, IProgress<ManagedModelProgress>, CancellationToken, Task> configure,
        Func<bool, string, IProgress<ManagedModelProgress>, CancellationToken, Task> configureAutomatically,
        Func<InitialSetupChoice, CancellationToken, Task> saveChoice)
    {
        _configure = configure; _configureAutomatically = configureAutomatically; _saveChoice = saveChoice;
        Title = CaptureUiText.Pick("开始使用 · 配置当前模型", "Get started · configure your current model");
        Width = 600; Height = 720; MinWidth = 440; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("workspace");
        Styles.Add(new WorkspaceStyles());
        _models.ItemsSource = ManagedMultimodalModels.All;
        _models.SelectedItem = ManagedMultimodalModels.Recommended;
        _backends.ItemsSource = ManagedRuntimeBackends.All;
        _backends.SelectedItem = ManagedRuntimeBackends.Default;
        _models.SelectionChanged += (_, _) => UpdateDetails();
        AutomaticButton.Content = CaptureUiText.Pick("一键检测并自动配置", "Detect and configure automatically");
        ManualOptions.Header = CaptureUiText.Pick("手动选择模型与后端", "Choose model and runtime manually");
        DownloadButton.Content = CaptureUiText.Pick("下载、验证并使用本地模型", "Download, verify and use a local model");
        ExistingButton.Content = CaptureUiText.Pick("我已有本机模型服务", "Connect an existing local service");
        ApiButton.Content = CaptureUiText.Pick("API 模型管理 · 本地或云端接口", "API models · local or cloud endpoints");
        CancelButton.Content = CaptureUiText.Pick("取消配置", "Cancel setup");
        AutomaticButton.Classes.Add("primary");
        foreach (var button in new[] { DownloadButton, ExistingButton, ApiButton, CancelButton }) button.Classes.Add("secondary");
        foreach (var button in new[] { AutomaticButton, DownloadButton, ExistingButton, ApiButton, CancelButton })
        {
            AutomationProperties.SetAutomationId(button, button.Name);
            AutomationProperties.SetName(button, button.Content?.ToString() ?? "");
        }
        AutomationProperties.SetAutomationId(ManualOptions, ManualOptions.Name);
        AutomationProperties.SetName(ManualOptions, ManualOptions.Header.ToString());
        CancelButton.IsVisible = false;
        AutomaticButton.Click += async (_, _) => { OperationTask = InstallAsync(automatic: true); await OperationTask; };
        DownloadButton.Click += async (_, _) => { OperationTask = InstallAsync(automatic: false); await OperationTask; };
        foreach (var button in new[] { ExistingButton, ApiButton })
            button.Click += async (_, _) => { OperationTask = ChooseAsync(InitialSetupChoice.ConfigureExisting); await OperationTask; };
        CancelButton.Click += (_, _) => _operation?.Cancel();
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        ManualOptions.Content = new StackPanel
        {
            Spacing = 12, Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                new TextBlock { Text = CaptureUiText.Pick("基础功能 · 本机多模态模型", "Built-in local multimodal model"), FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = CaptureUiText.Pick("本机模型", "Local model") }, _models,
                new TextBlock { Text = CaptureUiText.Pick("运行后端", "Runtime backend") }, _backends,
                _runtimeHost, _details,
                new TextBlock { Text = CaptureUiText.Pick("点击下方按钮应用手动选择，补齐缺失文件并验证识字与翻译。", "Apply your manual selections below, download missing files and verify recognition and translation."), TextWrapping = TextWrapping.Wrap },
                DownloadButton
            }
        };
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = CaptureUiText.Pick("使用软件基础功能部署本机模型，或在 API 模型管理中连接已有的本地／云端服务。所有任务共用一个支持图片输入和文字输出的当前模型。", "Use built-in deployment for a local model, or connect an existing local/cloud service under API models. All tasks share one current model supporting image input and text output."), TextWrapping = TextWrapping.Wrap },
                    new Border
                    {
                        Classes = { "workspace-inset" },
                        Child = new StackPanel { Spacing = 12, Children =
                        {
                            AutomaticButton,
                            new TextBlock { Text = CaptureUiText.Pick("自动检测显卡、准备后端与模型，验证识字和翻译后保存并开始使用。显卡不适用时自动选择 CPU。", "Detect hardware, prepare the runtime and model, then verify recognition and translation before saving. Use CPU when no GPU is usable."), TextWrapping = TextWrapping.Wrap },
                            new TextBlock { Text = CaptureUiText.Pick("优先复用已配置模型；首次使用推荐的 Qwen3.5 2B Q4（约 1.82 GiB，Apache-2.0）。仅点击配置按钮后下载缺失文件。", "Reuse the configured model; first-time setup uses Qwen3.5 2B Q4 (about 1.82 GiB, Apache-2.0). Missing files download only after you start setup."), TextWrapping = TextWrapping.Wrap }
                        } }
                    },
                    _progress, _status, CancelButton, ManualOptions, ApiButton, ExistingButton
                }
            }
        };
        UpdateDetails();
        if (!hasRuntime)
        {
            _status.Text = CaptureUiText.Pick("一键配置将准备推理后端与模型。也可在下方连接已有 API 服务。", "Automatic setup will prepare the runtime and model. You can also connect an existing API service below.");
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
        _runtimeHardware?.SetParentBusy(busy);
        AutomaticButton.IsEnabled = DownloadButton.IsEnabled = !busy;
        ExistingButton.IsEnabled = ApiButton.IsEnabled = _models.IsEnabled = _backends.IsEnabled = !busy;
        CancelButton.IsVisible = _progress.IsVisible = busy;
    }
    private async Task InstallAsync(bool automatic)
    {
        if (_operation is not null) return;
        if (_runtimeHardware?.IsBusy == true)
        {
            _status.Text = CaptureUiText.Pick("设备操作正在进行，请完成后再配置。", "Finish the current device operation before setup.");
            return;
        }
        using var operation = new CancellationTokenSource();
        _operation = operation;
        SetBusy(true);
        _progress.IsIndeterminate = true;
        _status.Text = automatic
            ? CaptureUiText.Pick("正在检测并准备本地模型…", "Detecting and preparing the local model…")
            : CaptureUiText.Pick("正在下载、校验、启动并测试 OCR 与翻译…", "Downloading, verifying, starting and testing OCR and translation…");
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_operation, operation) && IsVisible) CancelButton.BringIntoView();
        }, DispatcherPriority.Loaded);
        var ready = false;
        try
        {
            var progress = new Progress<ManagedModelProgress>(value =>
            {
                if (!ReferenceEquals(_operation, operation) || !IsVisible) return;
                _progress.IsIndeterminate = value.IsIndeterminate; _progress.Value = value.Percentage;
                _status.Text = UiText.T(value.Message);
            });
            if (automatic)
                await _configureAutomatically(_runtimeHardware?.AllowMirrors ?? false,
                    _runtimeHardware?.MirrorPrefixes ?? "", progress, operation.Token);
            else if (_models.SelectedItem is ManagedMultimodalModel model)
                await _configure(model, (_backends.SelectedItem as ManagedRuntimeBackend)?.Id ?? ManagedRuntimeBackends.Default.Id, progress, operation.Token);
            else return;
            ready = true;
        }
        catch (OperationCanceledException) { _status.Text = CaptureUiText.Pick("已取消，未完成配置；可以重试或配置其他模型。", "Cancelled. Setup was not completed; retry or configure another model."); }
        catch (Exception error) { _status.Text = UiText.Error(error); }
        finally { _operation = null; SetBusy(false); }
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

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
    private RuntimeSetupPanel? _runtimeHardware;
    private readonly StackPanel _runtimeHost = new() { Spacing = 8 };
    private readonly ComboBox _models = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly ComboBox _backends = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Height = 5, IsVisible = false };
    internal Button DownloadButton { get; } = new() { Name = "DownloadBasicButton" };
    internal Button LightweightButton { get; } = new() { Name = "UseLightweightButton" };
    internal Button ExistingButton { get; } = new() { Name = "UseExistingModelButton" };
    internal Button ApiButton { get; } = new() { Name = "ConfigureApiButton" };
    internal Button HybridButton { get; } = new() { Name = "ConfigureHybridButton" };
    internal Button CancelButton { get; } = new() { Name = "CancelModelSetupButton" };
    internal Task OperationTask { get; private set; } = Task.CompletedTask;
    private CancellationTokenSource? _operation;
    private bool _closeRequested;

    internal FirstRunSetupWindow(AppServices services) : this(services.ManagedModels.HasBundledRuntime,
        services.ConfigureInitialModelAsync, async (choice, token) =>
        {
            var settings = choice == InitialSetupChoice.Ready ? ProcessingModes.Apply(services.Settings, "lite") : services.Settings;
            await services.SaveSettingsAsync(settings with { InitialSetupCompleted = true }, token);
        })
    {
        _backends.SelectedItem = ManagedRuntimeBackends.Get(services.Settings.ManagedRuntimeBackend);
        _runtimeHardware = new RuntimeSetupPanel(services.ManagedModels.Runtimes, _backends, services.Settings,
            () => services.ManagedModels.CurrentRuntimeDescription, services);
        _runtimeHost.Children.Add(_runtimeHardware);
        _runtimeHardware.ActivityChanged += busy =>
        {
            DownloadButton.IsEnabled = !busy && _hasRuntime;
            LightweightButton.IsEnabled = ExistingButton.IsEnabled = ApiButton.IsEnabled = HybridButton.IsEnabled = !busy;
        };
        _configure = (model, backend, progress, token) => services.ConfigureInitialModelAsync(model, backend,
            _runtimeHardware.SelectedDevice, _runtimeHardware.AllowMirrors, _runtimeHardware.MirrorPrefixes, progress, token);
        Opened += async (_, _) => await _runtimeHardware.RefreshAsync();
        Closed += (_, _) => _runtimeHardware.Cancel();
        Height = 820;
    }

    internal FirstRunSetupWindow(bool hasRuntime,
        Func<ManagedMultimodalModel, string, IProgress<ManagedModelProgress>, CancellationToken, Task> configure,
        Func<InitialSetupChoice, CancellationToken, Task> saveChoice)
    {
        _hasRuntime = hasRuntime; _configure = configure; _saveChoice = saveChoice;
        Title = CaptureUiText.Pick("开始使用 · 选择处理模式", "Get started · choose a processing mode");
        Width = 600; Height = 720; MinWidth = 440; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("workspace");
        Styles.Add(new StyleInclude(new Uri("avares://PingYi.App/")) { Source = new Uri("avares://PingYi.App/Styles/Workspace.axaml") });
        _models.ItemsSource = ManagedMultimodalModels.All;
        _models.SelectedItem = ManagedMultimodalModels.Recommended;
        _backends.ItemsSource = ManagedRuntimeBackends.All;
        _backends.SelectedItem = ManagedRuntimeBackends.Default;
        _models.SelectionChanged += (_, _) => UpdateDetails();
        DownloadButton.Content = CaptureUiText.Pick("本地运行 · 一键下载并配置基础模式", "Local · download and configure Basic");
        LightweightButton.Content = CaptureUiText.Pick("暂不下载，使用轻量模式", "Skip download · use Lightweight");
        ExistingButton.Content = CaptureUiText.Pick("我已有本机模型服务", "Connect an existing local service");
        ApiButton.Content = CaptureUiText.Pick("第三方 API · 无需下载本地模型", "Third-party API · no local model download");
        HybridButton.Content = CaptureUiText.Pick("混合模式 · 每个任务独立选择服务", "Hybrid · choose a service for each task");
        CancelButton.Content = CaptureUiText.Pick("取消下载／启动", "Cancel download / startup");
        DownloadButton.Classes.Add("primary");
        foreach (var button in new[] { LightweightButton, ExistingButton, ApiButton, HybridButton, CancelButton }) button.Classes.Add("secondary");
        foreach (var button in new[] { DownloadButton, LightweightButton, ExistingButton, ApiButton, HybridButton, CancelButton })
        {
            AutomationProperties.SetAutomationId(button, button.Name);
            AutomationProperties.SetName(button, button.Content?.ToString() ?? "");
        }
        CancelButton.IsVisible = false;
        DownloadButton.Click += async (_, _) => { OperationTask = InstallAsync(); await OperationTask; };
        LightweightButton.Click += async (_, _) => { OperationTask = ChooseAsync(InitialSetupChoice.Ready); await OperationTask; };
        foreach (var button in new[] { ExistingButton, ApiButton, HybridButton })
            button.Click += async (_, _) => { OperationTask = ChooseAsync(InitialSetupChoice.ConfigureExisting); await OperationTask; };
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
                    new TextBlock { Text = CaptureUiText.Pick("选择本地、API 或混合方式。API/混合会打开配置中心：添加服务 → 获取模型 → 分配任务 → 测试并保存。只选择方式不会发送截图或执行推理。", "Choose local, API or hybrid. API/hybrid opens Configuration center: add a service → fetch models → assign tasks → test and save. Choosing a mode sends no screenshots or inference requests."), TextWrapping = TextWrapping.Wrap },
                    ApiButton, HybridButton,
                    new TextBlock { Text = CaptureUiText.Pick("本地基础模式：视觉 OCR ＋ 本机模型翻译", "Local Basic: vision OCR + model translation"), FontWeight = FontWeight.SemiBold },
                    _models, _backends, _runtimeHost, _details,
                    new TextBlock { Text = CaptureUiText.Pick("仅点击下载按钮才会下载并校验权重；API 模式不需要本地权重。已安装模型继续复用，退出时可释放后端资源。", "Weights are downloaded and verified only after clicking Download. API mode needs no local weights. Installed models are reused; exiting can release backend resources."), TextWrapping = TextWrapping.Wrap },
                    DownloadButton, _progress, _status, CancelButton, ExistingButton, LightweightButton
                }
            }
        };
        UpdateDetails();
        if (!hasRuntime)
        {
            DownloadButton.IsEnabled = false;
            _status.Text = CaptureUiText.Pick("当前目录缺少 llama.cpp 运行时。可使用 API、已有本机服务或轻量模式。", "This directory lacks llama.cpp. API, existing local services and Lightweight remain available.");
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
        DownloadButton.IsEnabled = !busy && _hasRuntime;
        LightweightButton.IsEnabled = ExistingButton.IsEnabled = ApiButton.IsEnabled = HybridButton.IsEnabled = _models.IsEnabled = _backends.IsEnabled = !busy;
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
            await _configure(model, (_backends.SelectedItem as ManagedRuntimeBackend)?.Id ?? ManagedRuntimeBackends.Default.Id, progress, _operation.Token);
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

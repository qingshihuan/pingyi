using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

/// <summary>Local detection is separate from the explicit network installation action.</summary>
internal sealed class RuntimeSetupPanel : StackPanel
{
    private readonly RuntimeManager? _manager;
    private readonly ComboBox _backend;
    private readonly Func<string> _running;
    private readonly TextBlock _inventory = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    internal ComboBox Devices { get; } = new() { Name = "ExecutionGpuCombo", HorizontalAlignment = HorizontalAlignment.Stretch };
    internal Button DetectButton { get; } = new() { Name = "DetectGpuButton" };
    internal Button InstallButton { get; } = new() { Name = "InstallRuntimeButton" };
    private readonly Button _cancel = new();
    private readonly CheckBox _mirrors = new();
    private readonly TextBox _customMirrors = new() { AcceptsReturn = true, MinHeight = 60, PlaceholderText = "https://your-trusted-relay.example/" };
    private readonly ProgressBar _progress = new() { Height = 4, IsVisible = false };
    private CancellationTokenSource? _work;
    private bool _parentBusy, _closed;
    private long _scanVersion;
    private string _selection;
    public string SelectedDevice => (Devices.SelectedItem as RuntimeDeviceChoice)?.Value ?? _selection;
    public bool AllowMirrors => _mirrors.IsChecked == true;
    public string MirrorPrefixes => _customMirrors.Text?.Trim() ?? "";

    public RuntimeSetupPanel(RuntimeManager? manager, ComboBox backend, AppSettings settings, Func<string>? running = null)
    {
        _manager = manager; _backend = backend; _running = running ?? (() => "");
        _selection = settings.ManagedRuntimeDevice;
        Spacing = 8;
        DetectButton.Content = Pick("检测／刷新显卡", "Detect / refresh GPUs");
        InstallButton.Content = Pick("下载安装／更新后端", "Install / update runtime");
        _cancel.Content = Pick("取消", "Cancel"); _cancel.IsVisible = false;
        _mirrors.Content = Pick("GitHub 超时时允许使用第三方备用源", "Allow third-party relays when GitHub times out");
        _mirrors.IsChecked = settings.RuntimeAllowMirrors;
        _customMirrors.Text = settings.RuntimeMirrorPrefixes;
        foreach (var button in new[] { DetectButton, InstallButton, _cancel }) button.Classes.Add("secondary");
        foreach (var control in new Control[] { Devices, DetectButton, InstallButton })
        {
            AutomationProperties.SetAutomationId(control, control.Name);
            AutomationProperties.SetName(control, control.Name);
        }
        _inventory.Classes.Add("workspace-help"); _status.Classes.Add("workspace-help");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _inventory.Text = Pick("尚未检测显卡。检测只读取本机设备，不访问网络。", "GPUs have not been detected. Detection is local and offline.");
        SetDeviceChoices([], null);
        Children.Add(new TextBlock { Text = Pick("显卡与运行后端", "GPU and inference runtime"), FontWeight = FontWeight.SemiBold });
        Children.Add(_inventory);
        Children.Add(new TextBlock { Text = Pick("执行显卡（所选后端的实际设备）", "Execution GPU (actual backend devices)") });
        Children.Add(Devices);
        Children.Add(new WrapPanel { Children = { DetectButton, InstallButton, _cancel } });
        Children.Add(_mirrors);
        Children.Add(new Expander { Header = Pick("自定义备用下载源（HTTPS 前缀，每行一个）", "Custom download relays (HTTPS prefixes, one per line)"), Content = _customMirrors });
        Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Text = Pick("仅点击下载时联网。备用源：ghfast.top、ghproxy.net；仅下载公开运行包，仍以官方 SHA-256 校验。CUDA／ROCm 需要兼容驱动，遵循其许可；不会自动安装系统驱动。旧后端保留，截图和凭据不发送给下载源。",
                        "Only Install accesses the network. Optional relays: ghfast.top and ghproxy.net; public runtime packages are checked against official SHA-256. CUDA/ROCm require compatible drivers and their license terms apply. System drivers are not installed; previous runtimes are retained. No captures or credentials are sent to download sources.") });
        Children.Add(_progress); Children.Add(_status);
        DetectButton.Click += async (_, _) => await RefreshAsync();
        InstallButton.Click += async (_, _) => await InstallAsync();
        _cancel.Click += (_, _) => _work?.Cancel();
        _backend.SelectionChanged += BackendChanged;
        Devices.SelectionChanged += (_, _) => { if (Devices.SelectedItem is RuntimeDeviceChoice choice) _selection = choice.Value; };
    }
    private static string Pick(string zh, string en) => UiText.IsEnglish ? en : zh;
    private async void BackendChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_closed || _parentBusy || _work is not null) return;
        _selection = "auto";
        await RefreshAsync();
    }
    public void SetParentBusy(bool busy) { _parentBusy = busy; SetControls(); }
    private void SetControls()
    {
        var ready = !_closed && !_parentBusy && _work is null;
        DetectButton.IsEnabled = InstallButton.IsEnabled = Devices.IsEnabled = _mirrors.IsEnabled = _customMirrors.IsEnabled = ready;
        _cancel.IsVisible = _work is not null;
        _progress.IsVisible = _work is not null;
    }
    public async Task RefreshAsync()
    {
        if (_manager is null || _work is not null || _closed || _parentBusy) return;
        var version = ++_scanVersion;
        _work = new CancellationTokenSource();
        SetControls();
        try { await RefreshCoreAsync(_work.Token); }
        catch (OperationCanceledException) { if (!_closed) _status.Text = Pick("检测已取消。", "Detection cancelled."); }
        catch (Exception error) { if (!_closed) _status.Text = Pick("设备检测未完成：", "Device detection did not complete: ") + UiText.Error(error); }
        finally { _work.Dispose(); _work = null; if (version == _scanVersion) SetControls(); }
    }
    private async Task RefreshCoreAsync(CancellationToken token)
    {
        if (_manager is null) return;
        var hardware = await _manager.DetectAsync(true, token);
        _inventory.Text = hardware.Count == 0 ? Pick("未能识别显卡；仍可手动选择 Vulkan 或 CPU。", "No GPU identified; Vulkan and CPU remain selectable.")
            : string.Join('\n', hardware.Select((gpu, i) => $"{i + 1}. {gpu}"));
        var backend = (_backend.SelectedItem as ManagedRuntimeBackend)?.Id ?? "auto";
        var runtime = await _manager.RecommendedInstalledAsync(backend, _selection, token);
        if (runtime is null)
        {
            SetDeviceChoices([], null);
            _status.Text = Pick("该后端尚未安装。点击下载安装后可选择其实际设备；不会把系统显卡序号直接用作运行时序号。", "This backend is not installed. Install it to select its actual devices; OS display indices are not runtime indices.");
            return;
        }
        var devices = runtime.Backend == "cpu" ? [] : await GpuInventory.ListDevicesAsync(runtime.Executable, runtime.Backend, token);
        SetDeviceChoices(devices, runtime.Backend);
        _status.Text = $"{runtime.Backend} · {runtime.Tag}\n" + _running() + "\n" +
            Pick("显卡选择需保存并应用。检测到设备清单变化时要求重新选择。", "Save and apply the GPU choice. Detected inventory changes require re-selection.");
    }
    internal void SetDeviceChoices(IReadOnlyList<RuntimeDevice> devices, string? backend)
    {
        var previous = _selection;
        var choices = new List<RuntimeDeviceChoice> { new("auto", Pick("自动选择可用显卡（CPU 模式不使用显卡）", "Select GPU automatically (not used in CPU mode)")) };
        choices.AddRange(devices.Where(d => d.IsHardwareGpu).Select(d => new RuntimeDeviceChoice(RuntimeDeviceChoice.Encode(d, devices), d.ToString())));
        if (previous != "auto" && choices.All(c => c.Value != previous))
            choices.Add(new(previous, Pick("已保存的设备需要重新检测／选择", "Saved device requires detection / re-selection")));
        Devices.ItemsSource = choices;
        Devices.SelectedItem = choices.First(c => c.Value == previous);
    }
    private async Task InstallAsync()
    {
        if (_manager is null || _closed || _parentBusy || _work is not null) return;
        _work = new CancellationTokenSource();
        SetControls();
        var oldBackendEnabled = _backend.IsEnabled;
        _backend.IsEnabled = false;
        try
        {
            var progress = new Progress<ManagedModelProgress>(p =>
            {
                if (_closed || _work is null) return;
                _progress.IsIndeterminate = p.IsIndeterminate; _progress.Value = p.Percentage;
                _status.Text = UiText.T(p.Message);
            });
            var runtime = await _manager.InstallAsync((_backend.SelectedItem as ManagedRuntimeBackend)?.Id ?? "auto", SelectedDevice,
                AllowMirrors, MirrorPrefixes.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), progress, _work.Token);
            await RefreshCoreAsync(_work.Token);
            _status.Text = Pick("后端已安装并通过启动／设备检查。点击模型的下载并配置／应用按钮验证推理后生效。", "Runtime installed and launch/device checks passed. Apply the model to verify inference and use it.") + $"\n{runtime.Backend} · {runtime.Tag}";
        }
        catch (OperationCanceledException) { _status.Text = Pick("已取消，保留可续传数据与原后端。", "Cancelled; resumable data and the previous runtime are retained."); }
        catch (Exception error) { _status.Text = UiText.Error(error); }
        finally { _work.Dispose(); _work = null; _backend.IsEnabled = oldBackendEnabled; SetControls(); }
    }
    public void Cancel()
    {
        _closed = true; _scanVersion++;
        _backend.SelectionChanged -= BackendChanged;
        _work?.Cancel();
    }
}

public partial class SettingsWindow
{
    private RuntimeSetupPanel? _runtimeHardware;
    private void InitializeRuntimeHardware()
    {
        if (_services is null || ManagedRuntimeBackendHintText.Parent is not Panel parent) return;
        _runtimeHardware = new RuntimeSetupPanel(_services.ManagedModels.Runtimes, ManagedRuntimeBackendCombo,
            _services.Settings, () => _services.ManagedModels.CurrentRuntimeDescription);
        parent.Children.Insert(parent.Children.IndexOf(ManagedRuntimeBackendHintText) + 1, _runtimeHardware);
        Opened += async (_, _) => await _runtimeHardware.RefreshAsync();
        Closed += (_, _) => _runtimeHardware.Cancel();
    }
}

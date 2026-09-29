using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PingYi.Core;

namespace PingYi.App;

/// <summary>Details use the same immutable snapshot as the home cards. No fake download/GPU health.</summary>
public sealed class RuntimeStatusView : UserControl
{
    internal RuntimeStatusSnapshot Snapshot { get; private set; } = RuntimeStatusSnapshot.Unknown;
    internal Button RefreshButton { get; } = new() { Name = "RefreshRuntimeStatusButton" };
    internal event EventHandler? RefreshRequested;
    internal event Action<int>? NavigateRequested;
    private readonly StackPanel _body = new() { Spacing = 14, Margin = new Thickness(28, 4, 28, 20) };
    private readonly TextBlock _title = new() { Classes = { "workspace-title" } };
    private readonly TextBlock _hint = new() { Classes = { "workspace-help" } };
    private readonly TextBlock _checked = new() { Classes = { "workspace-help" }, FontSize = 11 };
    private readonly StackPanel _details = new() { Spacing = 12 };
    private bool _busy, _refreshError;
    public RuntimeStatusView()
    {
        RefreshButton.Classes.Add("toolbar-action");
        RefreshButton.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        header.Children.Add(_title); Grid.SetColumn(RefreshButton, 1); header.Children.Add(RefreshButton);
        _body.Children.Add(header); _body.Children.Add(_hint); _body.Children.Add(_checked); _body.Children.Add(_details);
        Content = new ScrollViewer { Classes = { "workspace-page" }, Content = _body };
        AttachedToVisualTree += (_, _) => { UiText.LanguageChanged += LanguageChanged; Render(); };
        DetachedFromVisualTree += (_, _) => UiText.LanguageChanged -= LanguageChanged;
        Render();
    }
    private void LanguageChanged(object? sender, EventArgs e) => Render();
    internal void SetSnapshot(RuntimeStatusSnapshot snapshot) { Snapshot = snapshot; _refreshError = false; Render(); }
    internal void SetBusy(bool busy) { _busy = busy; RenderHeader(); }
    internal void SetError() { _refreshError = true; RenderHeader(); }
    private void RenderHeader()
    {
        _title.Text = ModeStatusText.Pick("运行状态", "Runtime status");
        _hint.Text = ModeStatusText.Pick("分别查看各模式是否配置、是否可用；与当前选择的处理方案无关。", "Availability for every mode, independent of the selected processing scheme.");
        RefreshButton.Content = _busy ? ModeStatusText.Pick("正在检查…", "Checking…") : WorkspaceText.Refresh;
        RefreshButton.IsEnabled = !_busy;
        _checked.Text = _refreshError ? ModeStatusText.Pick("状态检查未完成。保留上次结果，请重试。", "Status check did not complete. Previous results are retained; retry.") : Snapshot.CheckedAt is { } date
            ? ModeStatusText.Pick("上次检查：", "Last checked: ") + date.ToLocalTime().ToString("HH:mm:ss")
            : ModeStatusText.Pick("尚未检测", "Not checked yet");
    }
    private void Render()
    {
        RenderHeader(); _details.Children.Clear();
        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnSpacing = 12, RowSpacing = 12 };
        var entries = new Control[]
        {
            ModeDetail("lite", Snapshot.Lightweight, 1), ModeDetail("basic", Snapshot.Basic, 1),
            ModeDetail("cloud", Snapshot.Cloud, 2), RuntimeDetail(), ModelDetail(), GpuDetail()
        };
        for (var i = 0; i < entries.Length; i++) { Grid.SetColumn(entries[i], i % 2); Grid.SetRow(entries[i], i / 2); cards.Children.Add(entries[i]); }
        _details.Children.Add(cards);
        _details.Children.Add(new Border { Classes = { "workspace-inset" }, Child = new TextBlock
        {
            Classes = { "workspace-help" }, Text = ModeStatusText.Pick(
                "绿色：本地可用或已配置按需加载；红色：未配置或需要处理；灰色：未检测／待验证。\n下载服务未联网测试，不显示虚假的“连接正常”。刷新不会下载、启动视觉模型、执行推理或更改处理方案。云端验证由用户在相应设置页主动执行。",
                "Green: local readiness or configured on-demand loading. Red: not configured or needs attention. Gray: unknown or unverified.\nDownload connectivity has not been tested online. Refresh never downloads, starts a vision model, runs inference or changes your scheme. Cloud verification is an explicit action in its settings page.")
        } });
    }
    private Control ModeDetail(string id, ModeReadiness readiness, int tab)
    {
        var card = new ModeStatusCard(id); card.SetState(readiness);
        return Detail(card, ModeStatusText.Reason(readiness), ModeStatusText.Pick("配置／查看", "Configure / inspect"), tab);
    }
    private Control RuntimeDetail()
    {
        var runtime = Snapshot.Runtime;
        var text = Snapshot.CheckedAt is null ? ModeStatusText.Pick("尚未检测", "Not checked")
            : runtime is null ? ModeStatusText.Pick("所选后端未安装或不可用", "Selected runtime is not installed or available")
            : ManagedRuntimeBackends.Get(runtime.Backend).LocalizedDisplayName + " · " + runtime.Tag + "\n" +
              (Snapshot.Model?.Running == true ? Snapshot.Model.RuntimeDescription : ModeStatusText.Pick("已安装，不等同于模型正在运行", "Installed; this does not mean a model is running"));
        return Detail(Heading("运行后端", "Inference runtime"), text, ModeStatusText.Pick("管理后端", "Manage runtime"), 1);
    }
    private Control ModelDetail()
    {
        var model = Snapshot.Model;
        var text = Snapshot.CheckedAt is null ? ModeStatusText.Pick("尚未检测", "Not checked")
            : !RuntimePolicy.HasConfiguredManagedRuntime(Snapshot.Settings)
                ? ModeStatusText.Pick("尚未配置托管模型；外部本机服务由其自身管理。", "No managed model configured. External local servers manage their own models.")
                : model?.ModelName + "\n" + (model?.FilesPresent == true
                    ? ModeStatusText.Pick("文件存在且大小匹配；启动时进行完整校验。", "Files exist and sizes match; full integrity verification runs at startup.")
                    : ModeStatusText.Pick("模型／视觉组件不完整，请修复。", "Model / projector files are incomplete; repair them."));
        return Detail(Heading("本地模型", "Local model"), text ?? "", ModeStatusText.Pick("配置模型", "Configure model"), 1);
    }
    private Control GpuDetail()
    {
        var text = Snapshot.CheckedAt is null ? ModeStatusText.Pick("尚未检测", "Not checked")
            : Snapshot.Settings.ManagedRuntimeBackend == "cpu" ? ModeStatusText.Pick("CPU 模式，不使用执行显卡。", "CPU mode; no execution GPU is used.")
            : Snapshot.Model?.Running == true ? Snapshot.Model.RuntimeDescription
            : ModeStatusText.Pick("当前未运行托管模型，不能将已选显卡显示为正在使用。请在本地模型页检测／选择实际设备。", "No managed model is running. A saved GPU selection is not an active device. Detect / select actual devices in Local models.");
        return Detail(Heading("执行显卡", "Execution GPU"), text ?? "", ModeStatusText.Pick("查看／选择显卡", "Inspect / select GPU"), 1);
    }
    private static TextBlock Heading(string zh, string en) => new() { Text = ModeStatusText.Pick(zh, en), Classes = { "workspace-section" } };
    private Border Detail(Control heading, string text, string action, int tab)
    {
        var button = new Button { Content = action, Classes = { "secondary" }, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => NavigateRequested?.Invoke(tab);
        return new Border { Classes = { "workspace-card" }, Padding = new Thickness(14), Child = new StackPanel
        {
            Spacing = 10, Children = { heading, new TextBlock { Text = text, Classes = { "workspace-help" }, FontSize = 12 }, button }
        } };
    }
}

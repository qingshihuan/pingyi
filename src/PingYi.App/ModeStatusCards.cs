using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PingYi.Core;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace PingYi.App;

internal static class ModeStatusText
{
    internal static string Pick(string zh, string en) => UiText.IsEnglish ? en : zh;
    internal static string Title(string id) => id switch
    {
        "current" => Pick("当前模型", "Current model"),
        "basic" => Pick("本地模型", "Local models"),
        "api" => Pick("API 模型", "API models"),
        _ => id
    };
    internal static string Badge(ModeReadiness state) => state.State switch
    {
        ModeReadinessState.Available => Pick("可用", "Available"),
        ModeReadinessState.OnDemand => Pick("按需加载", "On demand"),
        ModeReadinessState.Unconfigured => Pick("未配置", "Not set"),
        ModeReadinessState.NeedsAttention => Pick("需处理", "Attention"),
        ModeReadinessState.Unverified => Pick("待验证", "Unverified"),
        ModeReadinessState.Verified => Pick("已验证", "Verified"),
        _ => Pick("未检测", "Not checked")
    };
    internal static string Reason(ModeReadiness state) => state.Reason switch
    {
        "basic-unconfigured" => Pick("尚未配置本地多模态模型。", "No local multimodal model is configured."),
        "current-unconfigured" => Pick("请测试图片和文字能力，然后选择当前模型。", "Test image and text support, then select the current model."),
        "current-tested" => Pick("图片与文字样例已验证；本次刷新未联网检查连通性或额度。", "Image and text samples verified; this refresh does not check live connectivity or quota."),
        "model-missing" => Pick("模型或视觉组件缺失／大小不符，请在本地模型中修复。", "Model or vision-projector files are missing or have the wrong size. Repair them in Local models."),
        "runtime-missing" => Pick("所选运行后端尚未安装，请在本地模型中管理后端。", "The selected runtime is not installed. Manage runtimes in Local models."),
        "basic-running" => Pick("已配置的托管模型服务正在运行；状态检查不进行额外推理。", "The configured managed model is running. Status checks do not perform extra inference."),
        "basic-on-demand" => Pick("本机模型与后端已配置，文件大小检查通过，当前未加载。截图时才校验并启动，不因刷新状态占用显存。", "Local model and runtime are configured and file sizes match; the model is not loaded. Capture verifies and starts it on demand. Refresh does not allocate model VRAM."),
        "local-configured" => Pick("已保存本机 API 服务；请在 API 模型管理中测试文字和图片能力。", "A local API service is configured; test text and vision under API models."),
        "local-unreachable" => Pick("已保存本机服务配置，但当前无法连接，请启动服务或检查端口。", "A local service is configured but is not reachable. Start it or check its port."),
        "api-unconfigured" => Pick("尚未保存 API 模型服务；不影响本地基础功能。", "No API model services are configured. Built-in local features are unaffected."),
        "api-unverified" => Pick("已保存本地或云端 API 配置；本次未请求服务。请在 API 模型管理中主动测试。", "Local or cloud API settings are present; no service was contacted during this refresh. Test explicitly under API models."),
        "api-tested" => Pick("已有 API 模型通过图片与文字能力验证；其他模型可能仍需测试。本次刷新未联网检查连通性或额度。", "An API model has passed image and text tests; other models may still need testing. This refresh does not check live connectivity or quota."),
        _ => Pick("尚未检测；不会仅凭已选模式显示为可用。", "Not checked. Selecting a mode alone does not make it available.")
    };
}

public sealed class ModeStatusCards : UserControl
{
    internal ModeStatusCard[] Cards { get; }
    internal RuntimeStatusSnapshot Snapshot { get; private set; } = RuntimeStatusSnapshot.Unknown;
    public ModeStatusCards()
    {
        Cards = [new("current"), new("basic"), new("api")];
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 12 };
        for (var i = 0; i < Cards.Length; i++) { Grid.SetColumn(Cards[i], i); grid.Children.Add(Cards[i]); }
        Content = grid;
        AttachedToVisualTree += (_, _) => { UiText.LanguageChanged += LanguageChanged; Render(); };
        DetachedFromVisualTree += (_, _) => UiText.LanguageChanged -= LanguageChanged;
        Render();
    }
    private void LanguageChanged(object? sender, EventArgs args) => Render();
    internal void SetSnapshot(RuntimeStatusSnapshot snapshot) { Snapshot = snapshot; Render(); }
    private void Render()
    {
        Cards[0].SetState(Snapshot.Current); Cards[1].SetState(Snapshot.Basic); Cards[2].SetState(Snapshot.Api);
    }
}

internal sealed class ModeStatusCard : Border
{
    internal string ModeId { get; }
    internal ModeReadiness State { get; private set; } = ModeReadiness.Unknown;
    internal TextBlock TitleText { get; } = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    internal TextBlock BadgeText { get; } = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    internal Border Indicator { get; } = new() { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _badge = new() { Padding = new Thickness(9, 5), CornerRadius = new CornerRadius(20), VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _layout = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 10, RowSpacing = 3 };
    public ModeStatusCard(string id)
    {
        ModeId = id; Name = id + "ModeStatusCard";
        Padding = new Thickness(14, 12); CornerRadius = new CornerRadius(12); BorderThickness = new Thickness(1);
        ThemeResources.Use(this, BackgroundProperty, "SubtleBackgroundBrush");
        ThemeResources.Use(this, BorderBrushProperty, "BorderBrush");
        var tile = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), VerticalAlignment = VerticalAlignment.Center };
        ThemeResources.Use(tile, BackgroundProperty, id == "current" ? "SuccessBackgroundBrush" : "BrandSoftBrush");
        var icon = new ShapePath { Width = 20, Height = 20, Stretch = Stretch.Uniform, StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Data = Geometry.Parse(id switch
            {
                "current" => "M5 2H14L20 8V22H5Z M14 2V8H20 M8 12H16 M8 16H13",
                "basic" => "M3 6L12 1L21 6V18L12 23L3 18Z M3 6L12 11L21 6 M12 11V23",
                _ => "M6 19H20C26 19 26 10 20 10C19 1 7 1 6 11C0 10 -1 19 6 19Z"
            }) };
        ThemeResources.Use(icon, ShapePath.StrokeProperty, id == "current" ? "SuccessBrush" : "BrandBrush");
        tile.Child = icon; Grid.SetRowSpan(tile, 2); _layout.Children.Add(tile);
        Grid.SetColumn(TitleText, 1); _layout.Children.Add(TitleText);
        _badge.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Indicator, BadgeText } };
        Grid.SetColumn(_badge, 2); _layout.Children.Add(_badge);
        Child = _layout;
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < 270;
            Grid.SetRow(_badge, compact ? 1 : 0); Grid.SetColumn(_badge, compact ? 1 : 2);
            _badge.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        };
        AutomationProperties.SetLiveSetting(BadgeText, AutomationLiveSetting.Polite);
        SetState(ModeReadiness.Unknown);
    }
    internal void SetState(ModeReadiness state)
    {
        State = state; TitleText.Text = ModeStatusText.Title(ModeId); BadgeText.Text = ModeStatusText.Badge(state);
        var error = state.State is ModeReadinessState.Unconfigured or ModeReadinessState.NeedsAttention;
        var verified = state.State == ModeReadinessState.Verified;
        ThemeResources.Use(Indicator, BackgroundProperty, verified ? "BrandBrush" : state.IsReady ? "SuccessBrush" : error ? "DangerBrush" : "TertiaryTextBrush");
        ThemeResources.Use(_badge, BackgroundProperty, verified ? "BrandSoftBrush" : state.IsReady ? "SuccessBackgroundBrush" : error ? "StatusDangerBackgroundBrush" : "HoverBackgroundBrush");
        ThemeResources.Use(BadgeText, TextBlock.ForegroundProperty, verified ? "BrandBrush" : state.IsReady ? "SuccessTextBrush" : error ? "DangerTextBrush" : "SecondaryTextBrush");
        var description = $"{TitleText.Text}：{BadgeText.Text}。{ModeStatusText.Reason(state)}";
        ToolTip.SetTip(this, description); AutomationProperties.SetName(this, description);
    }
}

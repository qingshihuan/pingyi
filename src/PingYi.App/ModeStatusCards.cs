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
        "lite" => Pick("轻量模式", "Lightweight"),
        "basic" => Pick("基础模式", "Basic mode"),
        "cloud" => Pick("云端模式", "Cloud mode"),
        _ => id
    };
    internal static string Badge(ModeReadiness state) => state.State switch
    {
        ModeReadinessState.Available => Pick("可用", "Available"),
        ModeReadinessState.OnDemand => Pick("按需加载", "On demand"),
        ModeReadinessState.Unconfigured => Pick("未配置", "Not set"),
        ModeReadinessState.NeedsAttention => Pick("需处理", "Attention"),
        ModeReadinessState.Unverified => Pick("待验证", "Unverified"),
        _ => Pick("未检测", "Not checked")
    };
    internal static string Reason(ModeReadiness state) => state.Reason switch
    {
        "lite-ready" => Pick("PaddleOCR 与 Argos 可用，支持本地探测及中英离线翻译。", "PaddleOCR and Argos are available for local detection and offline Chinese/English translation."),
        "lite-missing" => Pick("本地 OCR 或中英翻译组件未就绪，请检查本地模型。", "Local OCR or the Chinese/English translator is not ready. Check Local models."),
        "basic-unconfigured" => Pick("尚未配置本机视觉模型。可通过模型配置引导设置基础模式。", "No local vision model is configured. Use model setup to configure Basic mode."),
        "model-missing" => Pick("模型或视觉组件缺失／大小不符，请在本地模型中修复。", "Model or vision-projector files are missing or have the wrong size. Repair them in Local models."),
        "runtime-missing" => Pick("所选运行后端尚未安装，请在本地模型中管理后端。", "The selected runtime is not installed. Manage runtimes in Local models."),
        "basic-running" => Pick("已配置的托管模型服务正在运行；状态检查不进行额外推理。", "The configured managed model is running. Status checks do not perform extra inference."),
        "basic-on-demand" => Pick("本机模型与后端已配置，文件大小检查通过，当前未加载。截图时才校验并启动，不因刷新状态占用显存。", "Local model and runtime are configured and file sizes match; the model is not loaded. Capture verifies and starts it on demand. Refresh does not allocate model VRAM."),
        "local-vision-unverified" => Pick("本机服务可连接；模型列表不代表视觉 OCR 已验证，请在自定义接口测试图片能力。", "The local service is reachable. Its model list does not verify vision OCR; test image support in Custom endpoint."),
        "local-unreachable" => Pick("已保存本机服务配置，但当前无法连接，请启动服务或检查端口。", "A local service is configured but is not reachable. Start it or check its port."),
        "cloud-unconfigured" => Pick("尚未保存云端 OCR／翻译凭据或远程兼容接口；不影响本地功能。", "No cloud OCR/translation credentials or remote compatible endpoint are configured. Local features are unaffected."),
        "cloud-unverified" => Pick("已保存云端配置，但本次未联网验证凭据、额度或连通性。请在云端服务／自定义接口中主动验证。", "Cloud settings are present; credentials, quota and connectivity were not checked online. Verify explicitly in Cloud services or Custom endpoint."),
        "cloud-incomplete" => Pick("云端凭据不完整，请补齐 OCR 和翻译所需配置。", "Cloud credentials are incomplete. Complete the required OCR and translation settings."),
        "credentials-unreadable" => Pick("无法读取系统安全存储，云端状态未知；未显示或记录密钥。", "Secure-store access failed; cloud status is unknown. No keys are displayed or logged."),
        _ => Pick("尚未检测；不会仅凭已选模式显示为可用。", "Not checked. Selecting a mode alone does not make it available.")
    };
}

public sealed class ModeStatusCards : UserControl
{
    internal ModeStatusCard[] Cards { get; }
    internal RuntimeStatusSnapshot Snapshot { get; private set; } = RuntimeStatusSnapshot.Unknown;
    public ModeStatusCards()
    {
        Cards = [new("lite"), new("basic"), new("cloud")];
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
        Cards[0].SetState(Snapshot.Lightweight); Cards[1].SetState(Snapshot.Basic); Cards[2].SetState(Snapshot.Cloud);
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
        ThemeResources.Use(tile, BackgroundProperty, id == "lite" ? "SuccessBackgroundBrush" : "BrandSoftBrush");
        var icon = new ShapePath { Width = 20, Height = 20, Stretch = Stretch.Uniform, StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Data = Geometry.Parse(id switch
            {
                "lite" => "M5 2H14L20 8V22H5Z M14 2V8H20 M8 12H16 M8 16H13",
                "basic" => "M3 6L12 1L21 6V18L12 23L3 18Z M3 6L12 11L21 6 M12 11V23",
                _ => "M6 19H20C26 19 26 10 20 10C19 1 7 1 6 11C0 10 -1 19 6 19Z"
            }) };
        ThemeResources.Use(icon, ShapePath.StrokeProperty, id == "lite" ? "SuccessBrush" : "BrandBrush");
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
        ThemeResources.Use(Indicator, BackgroundProperty, state.IsReady ? "SuccessBrush" : error ? "DangerBrush" : "TertiaryTextBrush");
        ThemeResources.Use(_badge, BackgroundProperty, state.IsReady ? "SuccessBackgroundBrush" : error ? "StatusDangerBackgroundBrush" : "HoverBackgroundBrush");
        ThemeResources.Use(BadgeText, TextBlock.ForegroundProperty, state.IsReady ? "SuccessTextBrush" : error ? "DangerTextBrush" : "SecondaryTextBrush");
        var description = $"{TitleText.Text}：{BadgeText.Text}。{ModeStatusText.Reason(state)}";
        ToolTip.SetTip(this, description); AutomationProperties.SetName(this, description);
    }
}

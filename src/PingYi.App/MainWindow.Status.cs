using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using PingYi.Core;

namespace PingYi.App;

public partial class MainWindow
{
    private RuntimeStatusSnapshot _runtimeStatusSnapshot = RuntimeStatusSnapshot.Unknown;
    private readonly CancellationTokenSource _statusLifetime = new();
    private bool _statusClosed;
    private readonly TextBlock _modeRefreshLabel = new() { VerticalAlignment = VerticalAlignment.Center };

    private void InitializeModeStatusUi()
    {
        // Keep the overview above the fixed footer at 1040x720, including Windows
        // English font metrics. Compact only spacing and secondary action padding.
        ModeStatusBorder.Padding = new Thickness(18, 6);
        if (ModeStatusBorder.Child is Grid statusLayout) statusLayout.RowSpacing = 6;
        foreach (var card in ModeCards.Cards) card.Padding = new Thickness(14, 8);
        foreach (var button in new[] { RefreshWorkspaceButton, ModeDetailsButton })
        {
            button.MinHeight = 30;
            button.Padding = new Thickness(13, 3);
        }
        var refreshIcon = new Avalonia.Controls.Shapes.Path
        {
            Width = 14, Height = 14, Stretch = Stretch.Uniform, StrokeThickness = 1.8,
            Data = Geometry.Parse("M20 7A8 8 0 1 0 21 15 M20 2V7H15"),
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round
        };
        ThemeResources.Use(refreshIcon, Avalonia.Controls.Shapes.Path.StrokeProperty, "SecondaryTextBrush");
        RefreshWorkspaceButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { refreshIcon, _modeRefreshLabel }
        };
        ModeDetailsButton.BorderThickness = new Thickness(0);
        ModeDetailsButton.Background = Brushes.Transparent;
        RefreshModeStatusLanguage();
        Closed += (_, _) => { _statusClosed = true; _statusLifetime.Cancel(); };
    }
    private void RefreshModeStatusLanguage()
    {
        ModeStatusTitleText.Text = ModeStatusText.Pick("运行状态", "Runtime status");
        _modeRefreshLabel.Text = WorkspaceText.Refresh;
        ModeDetailsButton.Content = ModeStatusText.Pick("查看详情  ›", "View details  ›");
        ModeCards.SetSnapshot(_runtimeStatusSnapshot);
    }
    internal void RenderRuntimeStatus(RuntimeStatusSnapshot snapshot)
    {
        _runtimeStatusSnapshot = snapshot;
        RefreshModeStatusLanguage();
    }
    private async void OpenModeDetails_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Application.Current is App app && await app.OpenRuntimeStatusPageAsync()) return;
            var settings = _services is null ? new SettingsWindow() : new SettingsWindow(_services);
            settings.SelectRuntimeStatus();
            settings.Show(this);
        }
        catch (Exception error) { SetGlobalStatus(UiText.Error(error), true); }
    }
    private void RenderPipelineStatus(RuntimeStatusSnapshot snapshot)
    {
        var settings = snapshot.Settings;
        ModeReadiness PickProvider(string id) => id switch
        {
            "local-paddle" => ModeReadinessPolicy.Lightweight(snapshot.PaddleReady, snapshot.PaddleReady),
            "local-argos" => ModeReadinessPolicy.Lightweight(snapshot.ArgosReady, snapshot.ArgosReady),
            "local-vlm-ocr" or "custom-chat" => ProcessingModes.IsLocalEndpoint(settings) ? snapshot.Basic : snapshot.Cloud,
            _ => snapshot.Cloud
        };
        var ocr = PickProvider(settings.OcrProviderId);
        var translation = PickProvider(settings.TranslationProviderId);
        OcrHealthText.Text = ModeStatusText.Badge(ocr);
        TranslationHealthText.Text = ModeStatusText.Badge(translation);
        var broken = new[] { ocr, translation }.FirstOrDefault(state =>
            state.State is ModeReadinessState.NeedsAttention or ModeReadinessState.Unconfigured);
        if (broken is not null) { SetGlobalStatus(ModeStatusText.Reason(broken), true); return; }
        var onDemand = ocr.State == ModeReadinessState.OnDemand || translation.State == ModeReadinessState.OnDemand;
        var verified = ocr.IsReady && translation.IsReady;
        SetGlobalStatus(onDemand ? ModeStatusText.Reason(snapshot.Basic)
            : verified ? ModeStatusText.Pick("已就绪，点击“一键识别”或选择手动任务。", "Ready. Use Smart capture or choose a manual task.")
            : ModeStatusText.Pick("当前方案已配置但尚未验证；查看运行状态或前往设置检查。", "The current scheme is configured but unverified. View runtime status or check Settings."), false);
        // Never let a healthy mode card erase a shortcut-registration error.
        if (_services?.HotkeyRegistrationError is not null) return;
        if (onDemand || !verified)
        {
            var label = onDemand ? ModeStatusText.Pick("按需加载", "On demand") : ModeStatusText.Pick("待验证", "Unverified");
            TopStatusText.Text = label; LiveStatusTitleText.Text = label;
            TopStatusDot.Background = FindBrush(onDemand ? "SuccessBrushV2" : "TertiaryTextBrush");
            LiveStatusDot.Background = TopStatusDot.Background;
        }
    }
}

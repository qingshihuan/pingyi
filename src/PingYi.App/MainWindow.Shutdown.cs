using Avalonia.Automation;
using Avalonia.Interactivity;

namespace PingYi.App;

public partial class MainWindow
{
    internal Func<Task>? ExitRequested { get; set; }

    private void InitializeExitControl()
    {
        RefreshExitControl();
        UiText.LanguageChanged += ExitLanguageChanged;
        Closed += (_, _) => UiText.LanguageChanged -= ExitLanguageChanged;
    }

    private void ExitLanguageChanged(object? sender, EventArgs e) => RefreshExitControl();
    private void RefreshExitControl()
    {
        ExitApplicationButton.Content = CaptureUiText.Pick("退出并释放资源", "Quit and release resources");
        AutomationProperties.SetName(ExitApplicationButton, ExitApplicationButton.Content.ToString());
        Avalonia.Controls.ToolTip.SetTip(ExitApplicationButton, CaptureUiText.Pick(
            "关闭由截屏释义启动的模型后端；关闭窗口到托盘不等于退出。",
            "Stop backends started by Screen Insight. Hiding the window to the tray is not quitting."));
    }

    private async void ExitApplication_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ExitRequested is null || !ExitApplicationButton.IsEnabled) return;
        ExitApplicationButton.IsEnabled = false;
        await ExitRequested();
    }
}

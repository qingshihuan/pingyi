using Avalonia.Interactivity;

namespace PingYi.App;

public partial class SettingsWindow
{
    internal Func<CancellationToken, Task>? ResetCaptureApprovals { get; set; }
    internal Task ResetCaptureApprovalsTask { get; private set; } = Task.CompletedTask;

    private async void ResetCaptureApprovals_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ResetCaptureApprovals is null || !ResetCaptureApprovalsButton.IsEnabled) return;
        ResetCaptureApprovalsTask = ResetCaptureApprovalsAsync();
        await ResetCaptureApprovalsTask;
    }
    private async Task ResetCaptureApprovalsAsync()
    {
        if (ResetCaptureApprovals is null || !ResetCaptureApprovalsButton.IsEnabled) return;
        ResetCaptureApprovalsButton.IsEnabled = false;
        try
        {
            await ResetCaptureApprovals(CancellationToken.None);
            if (!_settingsClosed) SetGlobalStatus(CaptureUiText.Pick("已恢复确认提示，下次远程发送前将重新询问。", "Prompts restored. The next remote transfer will ask again."), false);
        }
        catch (Exception error) { if (!_settingsClosed) SetGlobalStatus(UiText.Error(error), true); }
        finally { if (!_settingsClosed) ResetCaptureApprovalsButton.IsEnabled = true; }
    }
}

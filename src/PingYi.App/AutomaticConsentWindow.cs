using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace PingYi.App;

/// <summary>Automatic routing cannot silently expand a screenshot's upload scope.</summary>
internal sealed class AutomaticConsentWindow : Window
{
    private AutomaticConsentWindow(string description)
    {
        Title = CaptureUiText.Pick("确认自动任务的数据发送", "Confirm data transfer for the automatic task");
        Width = 510; Height = 290; MinWidth = 400; MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var confirm = new Button { Content = CaptureUiText.Pick("同意并继续", "Allow and continue") };
        var cancel = new Button { Content = CaptureUiText.Pick("取消", "Cancel") };
        confirm.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 18,
            Children =
            {
                new TextBlock { Text = Title, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = CaptureUiText.Pick("仅授权本次任务；手动改选或重试需要重新确认。", "Applies to this request only. An automatic retry requires confirmation again."), TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Children = { cancel, confirm } }
            }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(false); } };
        Opened += (_, _) => cancel.Focus();
    }
    internal static async Task<bool> ConfirmAsync(Window owner, string description, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var dialog = new AutomaticConsentWindow(description);
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => { if (dialog.IsVisible) dialog.Close(false); }));
        var answer = await dialog.ShowDialog<bool>(owner);
        token.ThrowIfCancellationRequested();
        return answer;
    }
}

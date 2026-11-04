using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using PingYi.Core;

namespace PingYi.App;

/// <summary>Automatic routing cannot silently expand a screenshot's upload scope.</summary>
internal sealed class AutomaticConsentWindow : Window
{
    internal Button ConfirmButton { get; } = new() { Name = "AllowCaptureOnceButton" };
    internal Button RememberButton { get; } = new() { Name = "RememberCaptureApprovalButton" };
    internal Button CancelButton { get; } = new() { Name = "CancelCaptureApprovalButton" };
    internal AutomaticConsentWindow(string description)
    {
        Title = CaptureUiText.Pick("确认自动任务的数据发送", "Confirm data transfer for the automatic task");
        Width = 620; Height = 370; MinWidth = 440; MinHeight = 320;
        ShowInTaskbar = false;
        Classes.Add("workspace"); Styles.Add(new WorkspaceStyles());
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ConfirmButton.Content = CaptureUiText.Pick("同意并继续", "Allow once");
        RememberButton.Content = CaptureUiText.Pick("同意并不再提示", "Allow and remember");
        CancelButton.Content = CaptureUiText.Pick("取消", "Cancel");
        ConfirmButton.Classes.Add("secondary"); CancelButton.Classes.Add("secondary"); RememberButton.Classes.Add("primary");
        foreach (var button in new[] { CancelButton, ConfirmButton, RememberButton })
        {
            button.Margin = new Thickness(0, 0, 8, 8);
            AutomationProperties.SetAutomationId(button, button.Name);
            AutomationProperties.SetName(button, button.Content?.ToString() ?? "");
        }
        ConfirmButton.Click += (_, _) => Close(RemoteCaptureConsentChoice.AllowOnce);
        RememberButton.Click += (_, _) => Close(RemoteCaptureConsentChoice.AlwaysAllow);
        CancelButton.Click += (_, _) => Close(RemoteCaptureConsentChoice.Deny);
        var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 16 };
        layout.Children.Add(new TextBlock { Text = Title, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var body = new ScrollViewer { Content = new StackPanel { Spacing = 16, Children =
        {
            new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = CaptureUiText.Pick("“同意并继续”仅限本次。“同意并不再提示”会记住此服务、地址、协议与模型，后续截图识字、翻译和图片分析无需再次确认。可在“通用设置”恢复提示。",
                "Allow once applies only to this request. Allow and remember covers later screenshot recognition, translation and image analysis for this service, address, protocol and model. Reset prompts under General settings."), TextWrapping = TextWrapping.Wrap }
        } } };
        Grid.SetRow(body, 1); layout.Children.Add(body);
        var actions = new WrapPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { CancelButton, ConfirmButton, RememberButton } };
        Grid.SetRow(actions, 2); layout.Children.Add(actions); Content = layout;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(RemoteCaptureConsentChoice.Deny); } };
        Opened += (_, _) => CancelButton.Focus();
    }
    internal static async Task<RemoteCaptureConsentChoice> ConfirmAsync(Window owner, string description, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var dialog = new AutomaticConsentWindow(description);
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => { if (dialog.IsVisible) dialog.Close(RemoteCaptureConsentChoice.Deny); }));
        try
        {
            var answer = await dialog.ShowDialog<RemoteCaptureConsentChoice>(owner).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            return answer;
        }
        finally { dialog.Close(RemoteCaptureConsentChoice.Deny); }
    }
}

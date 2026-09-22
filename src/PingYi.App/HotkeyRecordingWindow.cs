using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using PingYi.Infrastructure;

namespace PingYi.App;

internal static class HotkeyInput
{
    // Keep the recorder's output within the keys supported by both native backends.
    internal static bool TryCreate(Key key, KeyModifiers modifiers, out string shortcut)
    {
        shortcut = string.Empty;
        const KeyModifiers supported = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift;
        if (modifiers == KeyModifiers.None || (modifiers & ~supported) != KeyModifiers.None)
            return false;
        var name = key.ToString();
        char letter;
        if (name.Length == 1 && char.IsAsciiLetter(name[0])) letter = name[0];
        else if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1])) letter = name[1];
        else return false;
        shortcut = (modifiers.HasFlag(KeyModifiers.Control) ? "Ctrl+" : "") +
                   (modifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : "") +
                   (modifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : "") + letter;
        _ = GlobalHotkeyGesture.Parse(shortcut);
        return true;
    }
}

internal sealed class HotkeyRecordingWindow : Window
{
    private readonly TextBox _preview = new() { IsReadOnly = true, FontSize = 20 };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _accept = new() { IsEnabled = false };
    internal string? Candidate { get; private set; }

    internal HotkeyRecordingWindow()
    {
        Title = UiText.IsEnglish ? "Record screenshot shortcut" : "录入截屏快捷键";
        Width = 520;
        Height = 290;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var instruction = new TextBlock
        {
            Text = UiText.IsEnglish
                ? "Press Ctrl, Alt or Shift together with a letter (A–Z) or number (0–9)."
                : "请同时按下 Ctrl、Alt 或 Shift 与一个字母（A–Z）或数字（0–9）。",
            TextWrapping = TextWrapping.Wrap
        };
        _hint.Text = UiText.IsEnglish
            ? "Enter accepts; Esc cancels. The shortcut is applied only after saving Settings."
            : "Enter 确认，Esc 取消。返回设置后点击保存才会应用。";
        AutomationProperties.SetName(_preview, Title);
        AutomationProperties.SetLiveSetting(_hint, Avalonia.Automation.Peers.AutomationLiveSetting.Polite);
        _accept.Content = UiText.IsEnglish ? "Use shortcut" : "使用此快捷键";
        _accept.Click += (_, _) => Close(Candidate);
        var cancel = new Button { Content = UiText.IsEnglish ? "Cancel" : "取消" };
        cancel.Click += (_, _) => Close((string?)null);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        buttons.Children.Add(_accept);
        buttons.Children.Add(cancel);
        var content = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        content.Children.Add(instruction);
        content.Children.Add(_preview);
        content.Children.Add(_hint);
        content.Children.Add(buttons);
        Content = content;
        AddHandler(KeyDownEvent, OnRecorderKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => _preview.Focus();
    }

    private void OnRecorderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab) return; // Keep keyboard access to both buttons.
        e.Handled = true;
        if (e.Key == Key.Escape) { Close((string?)null); return; }
        if (e.Key == Key.Enter)
        {
            if (Candidate is not null) Close(Candidate);
            return;
        }
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if (HotkeyInput.TryCreate(e.Key, e.KeyModifiers, out var shortcut))
        {
            Candidate = shortcut;
            _preview.Text = shortcut;
            _accept.IsEnabled = true;
            _hint.Text = UiText.IsEnglish ? "Press Enter to use this shortcut." : "按 Enter 使用此快捷键。";
        }
        else
        {
            Candidate = null;
            _preview.Text = string.Empty;
            _accept.IsEnabled = false;
            _hint.Text = UiText.IsEnglish
                ? "Unsupported combination. Use Ctrl, Alt or Shift + A–Z / 0–9 (not Super, function or keypad keys)."
                : "不支持此组合。请使用 Ctrl、Alt 或 Shift + A–Z / 0–9；暂不支持 Super、功能键或小键盘键。";
        }
    }
}

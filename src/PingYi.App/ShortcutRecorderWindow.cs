using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using PingYi.Infrastructure;

namespace PingYi.App;

public sealed class ShortcutRecorderWindow : Window
{
    private readonly TextBlock _preview;
    private readonly TextBlock _hint;
    private string? _candidate;
    private readonly HashSet<Key> _pressed = [];
    private bool _activated;

    public ShortcutRecorderWindow()
    {
        Title = UiText.IsEnglish ? "Record capture shortcut" : "录入截图快捷键";
        Width = 480;
        Height = 320;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("workspace");
        Styles.Add(new StyleInclude(new Uri("avares://PingYi.App/"))
            { Source = new Uri("avares://PingYi.App/Styles/Workspace.axaml") });
        _preview = new TextBlock
        {
            Name = "ShortcutPreview", FontSize = 28, FontWeight = FontWeight.SemiBold,
            Text = UiText.IsEnglish ? "Press a shortcut…" : "请按下快捷键…", TextWrapping = TextWrapping.Wrap
        };
        AutomationProperties.SetLiveSetting(_preview, AutomationLiveSetting.Polite);
        _hint = new TextBlock { Text = InputHint, TextWrapping = TextWrapping.Wrap };
        _hint.Classes.Add("workspace-help");
        var cancel = new Button { Content = UiText.IsEnglish ? "Cancel" : "取消", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        cancel.Classes.Add("secondary");
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Margin = new Thickness(28), Spacing = 18,
            Children = { _preview, _hint, cancel }
        };
        AddHandler(KeyDownEvent, RecordKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, RecordKeyUp, RoutingStrategies.Tunnel);
        Activated += (_, _) => _activated = true;
        Deactivated += (_, _) => { if (_activated && IsVisible) Close(null); };
    }

    internal static string InputHint => UiText.IsEnglish
        ? "Hold Ctrl, Alt or Shift and press one letter (A–Z) or digit (0–9). Release all keys to use it. Esc cancels. Save and apply in Settings to activate it."
        : "按住 Ctrl、Alt 或 Shift，再按一个字母（A–Z）或数字（0–9）。松开全部按键后填入设置；Esc 取消。最后点击“保存并应用”生效。";

    internal static string? GestureFromKey(Key key, KeyModifiers modifiers)
    {
        var allowed = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift;
        if ((modifiers & ~allowed) != 0 || (modifiers & allowed) == 0) return null;
        char letter;
        if (key >= Key.A && key <= Key.Z) letter = (char)('A' + (int)key - (int)Key.A);
        else if (key >= Key.D0 && key <= Key.D9) letter = (char)('0' + (int)key - (int)Key.D0);
        else return null;
        return new GlobalHotkeyGesture(modifiers.HasFlag(KeyModifiers.Control),
            modifiers.HasFlag(KeyModifiers.Alt), modifiers.HasFlag(KeyModifiers.Shift), letter).ToString();
    }

    private void RecordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(null); return; }
        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Tab or Key.Enter or Key.Space)
            return; // Keyboard access to Cancel remains available.
        e.Handled = true;
        _pressed.Add(e.Key);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
            return;
        _candidate = GestureFromKey(e.Key, e.KeyModifiers);
        if (_candidate is null)
        {
            _hint.Text = UiText.IsEnglish
                ? "Unsupported combination. Use Ctrl/Alt/Shift with one A–Z or 0–9 key; Super/Meta, function and keypad keys are not supported."
                : "不支持此组合。请使用 Ctrl/Alt/Shift 加一个 A–Z 或 0–9 键；暂不支持 Super/Meta、功能键或数字小键盘。";
            return;
        }
        _preview.Text = _candidate;
        _hint.Text = UiText.IsEnglish ? "Release all keys to continue." : "请松开全部按键。";
    }

    private void RecordKeyUp(object? sender, KeyEventArgs e)
    {
        _pressed.Remove(e.Key);
        if (_candidate is null) return;
        e.Handled = true;
        if (_pressed.Count == 0 && e.KeyModifiers == KeyModifiers.None) Close(_candidate);
    }
}

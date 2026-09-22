using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class SettingsWindow
{
    internal readonly Button RecordHotkeyButton = new() { Name = "RecordHotkeyButton" };
    internal readonly Button ResetHotkeyButton = new() { Name = "ResetHotkeyButton" };
    internal readonly TextBlock HotkeyEditorHint = new() { TextWrapping = TextWrapping.Wrap };
    private bool _useDefaultHotkey;
    private bool _hotkeyEditorBusy;

    private void InitializeHotkeyEditor()
    {
        RecordHotkeyButton.Classes.Add("secondary");
        ResetHotkeyButton.Classes.Add("secondary");
        RecordHotkeyButton.Margin = new Thickness(0, 0, 8, 6);
        ResetHotkeyButton.Margin = new Thickness(0, 0, 0, 6);
        HotkeyEditorHint.Classes.Add("workspace-help");
        AutomationProperties.SetLiveSetting(HotkeyEditorHint, AutomationLiveSetting.Polite);
        if (HotkeyBox.Parent is StackPanel host)
        {
            host.Children.Add(new WrapPanel { Children = { RecordHotkeyButton, ResetHotkeyButton } });
            host.Children.Add(HotkeyEditorHint);
        }
        RecordHotkeyButton.Click += RecordHotkey_OnClick;
        ResetHotkeyButton.Click += (_, _) =>
        {
            HotkeyBox.Text = AppSettings.DefaultHotkey;
            _useDefaultHotkey = true;
            RefreshHotkeyEditor();
        };
        HotkeyBox.TextChanged += (_, _) =>
        {
            if (!_isLoadingSettings) _useDefaultHotkey = false;
            RefreshHotkeyEditor();
        };
        RefreshHotkeyEditor();
    }

    private void RefreshHotkeyEditor()
    {
        RecordHotkeyButton.Content = UiText.IsEnglish ? "Record shortcut" : "录入快捷键";
        ResetHotkeyButton.Content = UiText.IsEnglish ? "Restore default" : "恢复默认";
        AutomationProperties.SetName(RecordHotkeyButton, RecordHotkeyButton.Content.ToString());
        AutomationProperties.SetName(ResetHotkeyButton, ResetHotkeyButton.Content.ToString());
        var text = UiText.IsEnglish
            ? $"Default: {AppSettings.DefaultHotkey}. Type a combination or record it, then select Save and apply. Ctrl/Alt/Shift + A–Z or 0–9."
            : $"默认：{AppSettings.DefaultHotkey}。可手动输入或录入组合，再点击“保存并应用”。支持 Ctrl/Alt/Shift 加 A–Z 或 0–9。";
        try
        {
            if (!string.IsNullOrWhiteSpace(HotkeyBox.Text) &&
                GlobalHotkeyGesture.Parse(HotkeyBox.Text).ToString() == "Ctrl+Shift+D")
                text += UiText.IsEnglish
                    ? " This overrides Chrome's bookmark-all-tabs shortcut while globally registered."
                    : " 全局注册后会覆盖 Chrome 的“收藏所有标签页”快捷键。";
        }
        catch (NotSupportedException)
        {
            text = UiText.IsEnglish
                ? "Invalid shortcut. Use Ctrl, Alt and/or Shift plus one letter or digit, without repeated modifiers or empty '+' parts."
                : "快捷键格式无效。请使用 Ctrl、Alt、Shift 中的一种或多种加一个字母或数字，不要重复修饰键或留空“+”分段。";
        }
        if (LinuxDesktopSession.IsWayland)
            text += UiText.IsEnglish
                ? " Wayland: this saves your preferred combination only. Bind the capture command to it in the desktop keyboard settings; PingYi cannot verify that system binding."
                : " Wayland：这里只保存首选组合；还需在桌面键盘设置中绑定下方截图命令。屏译无法确认系统绑定是否已生效。";
        HotkeyEditorHint.Text = text;
    }

    private async void RecordHotkey_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_hotkeyEditorBusy) return;
        _hotkeyEditorBusy = true;
        var saveEnabled = SaveSettingsButton.IsEnabled;
        RecordHotkeyButton.IsEnabled = ResetHotkeyButton.IsEnabled = SaveSettingsButton.IsEnabled = false;
        try
        {
            async Task<string?> Pick() => await new ShortcutRecorderWindow().ShowDialog<string?>(this);
            var selected = _services is null ? await Pick() : await _services.RecordHotkeyAsync(Pick);
            if (selected is not null) { HotkeyBox.Text = selected; _useDefaultHotkey = false; }
            if (_services?.HotkeyRegistrationError is { } error)
                SetGlobalStatus(LinuxDesktopUi.DescribeError(error), true);
            else
                SetGlobalStatus(UiText.IsEnglish
                    ? "The previous shortcut is unchanged. Select Save and apply to activate the new combination."
                    : "原快捷键未更改；点击“保存并应用”后启用新的组合。", false);
        }
        catch (Exception error) { SetGlobalStatus(LinuxDesktopUi.DescribeError(error), true); }
        finally
        {
            _hotkeyEditorBusy = false;
            RecordHotkeyButton.IsEnabled = ResetHotkeyButton.IsEnabled = true;
            SaveSettingsButton.IsEnabled = saveEnabled;
            RefreshHotkeyEditor();
        }
    }
}

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
    internal Button RecordShortcutButton { get; } = new() { Name = "RecordShortcutButton" };
    internal Button ResetShortcutButton { get; } = new() { Name = "ResetShortcutButton" };
    private readonly TextBlock _shortcutEditorHint = new() { TextWrapping = TextWrapping.Wrap };
    private bool _recordingShortcut;

    private void InitializeHotkeyEditor()
    {
        // Extend the existing form without replacing its named TextBox or save/rollback path.
        if (HotkeyBox.Parent is not StackPanel panel) return;
        var buttons = new WrapPanel();
        RecordShortcutButton.Classes.Add("secondary");
        ResetShortcutButton.Classes.Add("secondary");
        RecordShortcutButton.Margin = new Thickness(0, 0, 8, 8);
        ResetShortcutButton.Margin = new Thickness(0, 0, 0, 8);
        _shortcutEditorHint.Classes.Add("workspace-help");
        RecordShortcutButton.Click += RecordShortcut_OnClick;
        ResetShortcutButton.Click += (_, _) =>
        {
            if (_recordingShortcut || !SaveSettingsButton.IsEnabled) return;
            HotkeyBox.Text = AppSettings.DefaultHotkey;
            SetGlobalStatus(ShortcutSaveHint, false);
        };
        buttons.Children.Add(RecordShortcutButton);
        buttons.Children.Add(ResetShortcutButton);
        panel.Children.Add(buttons);
        panel.Children.Add(_shortcutEditorHint);
        RefreshHotkeyEditor();
        Deactivated += (_, _) => (Application.Current as App)?.RefreshHotkeyTooltip();
        Closed += (_, _) => (Application.Current as App)?.RefreshHotkeyTooltip();
    }

    private static string ShortcutSaveHint => LinuxDesktopSession.IsWayland
        ? UiText.IsEnglish
            ? "Save this preference, then bind the same shortcut to the capture command in desktop Settings. PingYi cannot register it directly on Wayland."
            : "请保存此偏好，再到系统设置中将相同快捷键绑定到截图命令；Wayland 下屏译不能直接注册全局快捷键。"
        : UiText.IsEnglish
            ? "Click Save to apply the shortcut. If registration fails, PingYi tries to restore the previous binding."
            : "点击保存后应用快捷键；注册失败时会尝试恢复原快捷键。";

    private void RefreshHotkeyEditor()
    {
        RecordShortcutButton.Content = UiText.IsEnglish ? "Record shortcut…" : "录入快捷键…";
        ResetShortcutButton.Content = UiText.IsEnglish ? "Restore default" : "恢复默认";
        AutomationProperties.SetName(RecordShortcutButton, RecordShortcutButton.Content.ToString());
        AutomationProperties.SetName(ResetShortcutButton, ResetShortcutButton.Content.ToString());
        _shortcutEditorHint.Text = ShortcutSaveHint;
    }

    private async void RecordShortcut_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_recordingShortcut || !SaveSettingsButton.IsEnabled) return;
        _recordingShortcut = true;
        // Include the asynchronous stop/restore intervals, not just the modal dialog.
        // Cancel old feedback timers so they cannot re-enable Save during restoration.
        CancelButtonFeedbackReset(SaveSettingsButton);
        SaveSettingsButton.IsEnabled = false;
        RecordShortcutButton.IsEnabled = false;
        ResetShortcutButton.IsEnabled = false;
        var suspended = false;
        try
        {
            if (_services is not null && _services.HotkeyService is not DesktopManagedHotkeyService)
            {
                // The existing global shortcut must be recordable without starting a capture.
                await _services.HotkeyService.StopAsync();
                suspended = true;
            }
            if (!IsVisible) return;
            var selected = await new HotkeyRecordingWindow().ShowDialog<string?>(this);
            if (selected is not null)
            {
                HotkeyBox.Text = selected;
                SetGlobalStatus(ShortcutSaveHint, false);
            }
        }
        catch (Exception exception)
        {
            SetGlobalStatus(LinuxDesktopUi.DescribeError(exception), true);
        }
        finally
        {
            // Restore the SAVED binding after confirmation, cancellation or exceptions,
            // never the unsaved value currently displayed in the text field.
            if (suspended && _services is not null)
            {
                try
                {
                    await _services.HotkeyService.StartAsync(_services.Settings.Hotkey);
                    _services.HotkeyRegistrationError = null;
                }
                catch (Exception exception)
                {
                    _services.HotkeyRegistrationError = exception;
                    SetGlobalStatus(LinuxDesktopUi.DescribeError(exception), true);
                }
            }
            _recordingShortcut = false;
            SaveSettingsButton.IsEnabled = true;
            RecordShortcutButton.IsEnabled = true;
            ResetShortcutButton.IsEnabled = true;
        }
    }
}

public partial class App
{
    internal void RefreshHotkeyTooltip()
    {
        if (_trayIcon is not null && _services is not null && !_isExiting)
            _trayIcon.ToolTipText = $"{(UiText.IsEnglish ? "PingYi" : AppEdition.ProductName)} · {_services.Settings.Hotkey}";
    }
}

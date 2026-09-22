using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class SettingsWindow
{
    private readonly Button _recordHotkeyButton = new() { Name = "RecordHotkeyButton" };
    private readonly Button _resetHotkeyButton = new() { Name = "ResetHotkeyButton" };
    private readonly Button _applyHotkeyButton = new() { Name = "ApplyHotkeyButton" };
    private readonly TextBlock _hotkeyFeedbackText = new() { Name = "HotkeyFeedbackText", TextWrapping = TextWrapping.Wrap };
    private HotkeyRecordingSession? _hotkeyRecordingSession;
    private bool _hotkeyRecordingRequested, _hotkeyBusy, _hotkeyWindowClosed;
    private bool _refreshingHotkeyControls, _ownsSaveDisable, _settingHotkeyDraft, _hotkeyResetRequested;
    private string? _hotkeyOriginalDraft, _pendingHotkey;
    private Key? _pendingHotkeyKey;
    private Exception? _hotkeyEditorError;

    private void InitializeHotkeyEditor()
    {
        // XAML's name scope is already complete. Dynamic controls use their logical tree
        // names and automation IDs, not late registrations that would break window startup.
        var parent = (StackPanel)HotkeyBox.Parent!;
        var actions = new WrapPanel();
        foreach (var button in new[] { _recordHotkeyButton, _resetHotkeyButton, _applyHotkeyButton })
        {
            button.Classes.Add("secondary");
            button.Margin = new Thickness(0, 4, 8, 4);
            AutomationProperties.SetAutomationId(button, button.Name);
            actions.Children.Add(button);
        }
        _hotkeyFeedbackText.Classes.Add("workspace-help");
        _hotkeyFeedbackText.FontSize = 12;
        AutomationProperties.SetLiveSetting(_hotkeyFeedbackText, AutomationLiveSetting.Polite);
        parent.Children.Add(actions);
        parent.Children.Add(_hotkeyFeedbackText);
        HotkeyBox.Text = AppSettings.DefaultHotkey;
        _recordHotkeyButton.Click += async (_, _) =>
        {
            if (_hotkeyRecordingRequested) await FinishHotkeyRecordingAsync(false);
            else await BeginHotkeyRecordingAsync();
        };
        _resetHotkeyButton.Click += (_, _) =>
        {
            SetHotkeyDraft(AppSettings.DefaultHotkey);
            _hotkeyResetRequested = true;
            _hotkeyEditorError = null;
            RefreshHotkeyEditor();
        };
        _applyHotkeyButton.Click += async (_, _) => await ApplyHotkeyAsync();
        // PropertyChanged is synchronous: reject invalid input before a subsequent Ctrl+S,
        // and distinguish programmatic draft updates from actual typing.
        HotkeyBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            if (!_settingHotkeyDraft)
            {
                _hotkeyResetRequested = false;
                _hotkeyEditorError = null;
            }
            RefreshHotkeyEditor();
        };
        HotkeyBox.LostFocus += async (_, _) =>
        {
            if (_hotkeyRecordingRequested) await FinishHotkeyRecordingAsync(false);
            else if (HotkeyDefinition.TryParse(HotkeyBox.Text, out var value)) SetHotkeyDraft(value.ToString());
        };
        AddHandler(KeyDownEvent, HotkeyRecorder_OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, HotkeyRecorder_OnKeyUp, RoutingStrategies.Tunnel);
        Deactivated += async (_, _) =>
        {
            if (_hotkeyRecordingRequested) await FinishHotkeyRecordingAsync(false);
        };
        Closed += async (_, _) =>
        {
            _hotkeyWindowClosed = true;
            if (_hotkeyRecordingRequested) await FinishHotkeyRecordingAsync(false);
        };
        SaveSettingsButton.PropertyChanged += (_, e) =>
        {
            if (e.Property != IsEnabledProperty || _refreshingHotkeyControls) return;
            RefreshHotkeyEditor();
            if (SaveSettingsButton.IsEnabled) (Application.Current as App)?.RefreshHotkeyPresentation();
        };
        Opened += (_, _) => RefreshHotkeyEditor();
        RefreshHotkeyEditor();
    }

    private void SetHotkeyDraft(string? value)
    {
        _settingHotkeyDraft = true;
        try { HotkeyBox.Text = value; }
        finally { _settingHotkeyDraft = false; }
    }

    private void RefreshHotkeyEditor()
    {
        if (_refreshingHotkeyControls) return;
        _refreshingHotkeyControls = true;
        try
        {
            var valid = HotkeyDefinition.TryParse(HotkeyBox.Text, out var gesture);
            var blocked = _hotkeyBusy || _hotkeyRecordingRequested || !valid;
            if (blocked && SaveSettingsButton.IsEnabled)
            {
                _ownsSaveDisable = true;
                SaveSettingsButton.IsEnabled = false;
            }
            else if (!blocked && _ownsSaveDisable)
            {
                _ownsSaveDisable = false;
                SaveSettingsButton.IsEnabled = true;
            }
            var otherSaveBusy = !SaveSettingsButton.IsEnabled && !_ownsSaveDisable;
            _recordHotkeyButton.Content = UiText.IsEnglish
                ? (_hotkeyRecordingRequested ? "Cancel recording" : "Record shortcut")
                : (_hotkeyRecordingRequested ? "取消录制" : "录制快捷键");
            _resetHotkeyButton.Content = UiText.IsEnglish ? "Restore default" : "恢复默认";
            _applyHotkeyButton.Content = LinuxDesktopSession.IsWayland
                ? (UiText.IsEnglish ? "Save preferred key" : "保存偏好快捷键")
                : (UiText.IsEnglish ? "Apply shortcut" : "应用快捷键");
            _recordHotkeyButton.IsEnabled = !_hotkeyBusy && !otherSaveBusy;
            _resetHotkeyButton.IsEnabled = !_hotkeyBusy && !_hotkeyRecordingRequested && !otherSaveBusy;
            _applyHotkeyButton.IsEnabled = !blocked && !otherSaveBusy;
            HotkeyBox.IsReadOnly = _hotkeyBusy || _hotkeyRecordingRequested || otherSaveBusy;
            foreach (var button in new[] { _recordHotkeyButton, _resetHotkeyButton, _applyHotkeyButton })
                AutomationProperties.SetName(button, button.Content?.ToString() ?? "");
            var current = _services?.Settings.Hotkey ?? AppSettings.DefaultHotkey;
            _hotkeyFeedbackText.Text = _hotkeyEditorError is not null
                ? LinuxDesktopUi.DescribeError(_hotkeyEditorError)
                : _hotkeyRecordingRequested
                    ? (UiText.IsEnglish ? "Press Ctrl, Alt or Shift with A–Z or 0–9, then release the key. Esc cancels."
                        : "请按 Ctrl、Alt 或 Shift 加 A–Z 或 0–9，然后松开按键。Esc 取消。")
                    : !valid
                        ? (UiText.IsEnglish ? "Use Ctrl, Alt or Shift plus one A–Z / 0–9 key. Empty or repeated parts, Super and function keys are not supported."
                            : "请输入 Ctrl、Alt 或 Shift 加一个字母或数字；不支持空项、重复项、Super 或功能键。")
                        : LinuxDesktopSession.IsWayland
                            ? (UiText.IsEnglish ? $"Preferred: {gesture}. Set the actual binding in the desktop keyboard settings below."
                                : $"偏好组合：{gesture}。实际绑定请在下方说明的系统键盘设置中完成。")
                            : (UiText.IsEnglish ? $"Saved: {current}. Apply to register and save {gesture}; failure restores the previous shortcut."
                                : $"已保存：{current}。点击应用注册并保存 {gesture}；失败将恢复旧快捷键。");
            ThemeResources.Use(_hotkeyFeedbackText, TextBlock.ForegroundProperty,
                !valid || _hotkeyEditorError is not null ? "DangerTextBrush" : "SecondaryTextBrush");
        }
        finally { _refreshingHotkeyControls = false; }
    }

    internal async Task BeginHotkeyRecordingAsync()
    {
        if (_hotkeyBusy || _hotkeyRecordingRequested || _hotkeyWindowClosed) return;
        _hotkeyOriginalDraft = HotkeyBox.Text;
        _pendingHotkey = null;
        _pendingHotkeyKey = null;
        _hotkeyEditorError = null;
        _hotkeyRecordingRequested = true;
        _hotkeyBusy = true;
        var session = new HotkeyRecordingSession(_services?.HotkeyService,
            _services?.Settings.Hotkey ?? AppSettings.DefaultHotkey);
        _hotkeyRecordingSession = session;
        RefreshHotkeyEditor();
        try
        {
            await session.BeginAsync();
            // Closing/deactivating while StopAsync was in flight still restores the binding.
            if (!_hotkeyRecordingRequested || _hotkeyWindowClosed) await session.EndAsync();
            else HotkeyBox.Focus();
        }
        catch (Exception error)
        {
            _hotkeyRecordingRequested = false;
            _hotkeyEditorError = error;
            if (_services is not null) _services.HotkeyRegistrationError = error;
        }
        finally
        {
            _hotkeyBusy = false;
            RefreshHotkeyEditor();
        }
    }

    internal async Task FinishHotkeyRecordingAsync(bool accept)
    {
        var session = _hotkeyRecordingSession;
        if (!_hotkeyRecordingRequested || session is null) return;
        _hotkeyRecordingRequested = false;
        _hotkeyBusy = true;
        SetHotkeyDraft(accept && _pendingHotkey is not null ? _pendingHotkey : _hotkeyOriginalDraft);
        if (accept) _hotkeyResetRequested = false;
        RefreshHotkeyEditor();
        try
        {
            await session.EndAsync();
            if (_services is not null) _services.HotkeyRegistrationError = null;
        }
        catch (Exception error)
        {
            _hotkeyEditorError = error;
            if (_services is not null) _services.HotkeyRegistrationError = error;
        }
        finally
        {
            _hotkeyRecordingSession = null;
            _hotkeyBusy = false;
            RefreshHotkeyEditor();
            if (!_hotkeyWindowClosed && IsActive) _recordHotkeyButton.Focus();
        }
    }

    private async void HotkeyRecorder_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_hotkeyRecordingRequested && !_hotkeyBusy) return;
        e.Handled = true; // In particular Ctrl+S must record, not save the settings form.
        if (!_hotkeyRecordingRequested || _hotkeyBusy) return;
        if (e.Key is Key.Escape or Key.Tab)
        {
            await FinishHotkeyRecordingAsync(false);
            return;
        }
        if (TryRecordHotkey(e.Key, e.KeyModifiers, out var shortcut))
        {
            _pendingHotkey = shortcut;
            _pendingHotkeyKey = e.Key;
            SetHotkeyDraft(shortcut);
        }
    }

    private async void HotkeyRecorder_OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_hotkeyRecordingRequested) return;
        e.Handled = true;
        // Restore after key-up, not key-down, to avoid an immediate repeat screenshot.
        if (_pendingHotkeyKey == e.Key) await FinishHotkeyRecordingAsync(true);
    }

    internal static bool TryRecordHotkey(Key key, KeyModifiers modifiers, out string shortcut)
    {
        shortcut = string.Empty;
        if ((modifiers & ~(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)) != 0) return false;
        var character = key >= Key.A && key <= Key.Z ? (char)('A' + (int)key - (int)Key.A)
            : key >= Key.D0 && key <= Key.D9 ? (char)('0' + (int)key - (int)Key.D0) : '\0';
        if (character == '\0' || modifiers == KeyModifiers.None) return false;
        shortcut = new HotkeyDefinition(modifiers.HasFlag(KeyModifiers.Control),
            modifiers.HasFlag(KeyModifiers.Alt), modifiers.HasFlag(KeyModifiers.Shift), character).ToString();
        return true;
    }

    private async Task ApplyHotkeyAsync()
    {
        if (_services is null || _hotkeyBusy || _hotkeyRecordingRequested ||
            !HotkeyDefinition.TryParse(HotkeyBox.Text, out var gesture)) return;
        var next = gesture.ToString();
        var previous = _services.Settings.Hotkey;
        _hotkeyBusy = true;
        _hotkeyEditorError = null;
        RefreshHotkeyEditor();
        try
        {
            await HotkeyRebinding.ApplyAsync(_services.HotkeyService, previous, next,
                () => _services.SaveSettingsAsync(_services.Settings with
                {
                    Hotkey = next,
                    HotkeyIsCustomized = !_hotkeyResetRequested
                }),
                error => _services.HotkeyRegistrationError = error);
            SetHotkeyDraft(next);
            (Application.Current as App)?.RefreshHotkeyPresentation();
            SetGlobalStatus(LinuxDesktopSession.IsWayland ? LinuxDesktopUi.ExternalShortcutHelp
                : UiText.IsEnglish ? $"Shortcut {next} saved and applied." : $"快捷键 {next} 已保存并生效。", false);
        }
        catch (Exception error)
        {
            _hotkeyEditorError = error;
            SetGlobalStatus(LinuxDesktopUi.DescribeError(error), true);
        }
        finally
        {
            _hotkeyBusy = false;
            RefreshHotkeyEditor();
        }
    }
}

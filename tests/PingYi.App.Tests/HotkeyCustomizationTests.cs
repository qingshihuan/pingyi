using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using PingYi.App;
using PingYi.Core;
using PingYi.Infrastructure;
using Xunit;

namespace PingYi.App.Tests;

public class HotkeyCustomizationTests
{
    [Theory]
    [InlineData(Key.D, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+D")]
    [InlineData(Key.G, KeyModifiers.Control | KeyModifiers.Alt, "Ctrl+Alt+G")]
    [InlineData(Key.D0, KeyModifiers.Alt | KeyModifiers.Shift, "Alt+Shift+0")]
    [InlineData(Key.D9, KeyModifiers.Control, "Ctrl+9")]
    public void Recorder_formats_only_native_supported_combinations(Key key, KeyModifiers modifiers, string expected)
    {
        Assert.True(HotkeyInput.TryCreate(key, modifiers, out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal(GlobalHotkeyGesture.Parse(expected), GlobalHotkeyGesture.Parse(actual));
    }

    [Theory]
    [InlineData(Key.D, KeyModifiers.None)]
    [InlineData(Key.D, KeyModifiers.Meta | KeyModifiers.Control)]
    [InlineData(Key.F1, KeyModifiers.Control)]
    [InlineData(Key.NumPad1, KeyModifiers.Control)]
    [InlineData(Key.LeftShift, KeyModifiers.Shift)]
    public void Unsupported_keys_are_not_silently_rebound(Key key, KeyModifiers modifiers) =>
        Assert.False(HotkeyInput.TryCreate(key, modifiers, out _));

    [AvaloniaTheory]
    [InlineData("zh-CN", "恢复默认")]
    [InlineData("en-US", "Restore default")]
    public void Settings_exposes_recorder_and_reset_without_immediate_persistence(string language, string resetLabel)
    {
        UiText.Configure(language);
        var window = new SettingsWindow();
        try
        {
            Assert.Equal(resetLabel, window.ResetShortcutButton.Content);
            var box = window.FindControl<TextBox>("HotkeyBox")!;
            box.Text = "Ctrl+Alt+G";
            window.ResetShortcutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(AppSettings.DefaultHotkey, box.Text);
            Assert.False(box.IsReadOnly);
            UiText.Configure(language == "en-US" ? "zh-CN" : "en-US");
            Assert.NotEqual(resetLabel, window.ResetShortcutButton.Content);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Recording_requires_confirmation_and_escape_cancels()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new HotkeyRecordingWindow();
        try
        {
            var result = dialog.ShowDialog<string?>(owner);
            dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.D, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift });
            Assert.Equal("Ctrl+Shift+D", dialog.Candidate);
            Assert.False(result.IsCompleted);
            dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Null(await result);
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task Enter_returns_the_recorded_shortcut()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new HotkeyRecordingWindow();
        try
        {
            var result = dialog.ShowDialog<string?>(owner);
            dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.G, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt });
            dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal("Ctrl+Alt+G", await result);
        }
        finally { dialog.Close(); owner.Close(); }
    }
}

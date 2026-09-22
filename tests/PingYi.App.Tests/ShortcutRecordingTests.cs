using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public sealed class ShortcutRecordingTests
{
    [Theory]
    [InlineData(Key.D, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+D")]
    [InlineData(Key.G, KeyModifiers.Control | KeyModifiers.Alt, "Ctrl+Alt+G")]
    [InlineData(Key.D9, KeyModifiers.Alt, "Alt+9")]
    [InlineData(Key.A, KeyModifiers.None, null)]
    [InlineData(Key.D, KeyModifiers.Meta | KeyModifiers.Control, null)]
    [InlineData(Key.F12, KeyModifiers.Control, null)]
    [InlineData(Key.NumPad1, KeyModifiers.Control, null)]
    public void Recorder_accepts_only_supported_combinations(Key key, KeyModifiers modifiers, string? expected) =>
        Assert.Equal(expected, ShortcutRecorderWindow.GestureFromKey(key, modifiers));

    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void Settings_exposes_record_reset_and_localized_help_without_losing_edits(string language)
    {
        UiText.Configure(language);
        var window = new SettingsWindow();
        try
        {
            window.Show();
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 4;
            var box = window.FindControl<TextBox>("HotkeyBox")!;
            box.Text = "Ctrl+Alt+G";
            Assert.False(box.IsReadOnly);
            Assert.True(window.RecordHotkeyButton.IsVisible);
            Assert.True(window.ResetHotkeyButton.IsVisible);
            Assert.Equal(language == "en-US" ? "Record shortcut" : "录入快捷键", window.RecordHotkeyButton.Content);
            UiText.Configure(language == "en-US" ? "zh-CN" : "en-US");
            Assert.Equal("Ctrl+Alt+G", box.Text);
            UiText.Configure(language);
            window.ResetHotkeyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(AppSettings.DefaultHotkey, box.Text);
            box.Text = "Ctrl++D";
            Assert.Contains(language == "en-US" ? "Invalid" : "无效", window.HotkeyEditorHint.Text);
            box.Text = "Ctrl+Shift+D";
            Assert.Contains("Chrome", window.HotkeyEditorHint.Text);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(directory, "hotkeys-settings-" + language + ".png"));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recorder_waits_for_primary_and_modifiers_in_either_release_order(bool modifierFirst)
    {
        UiText.Configure("en-US");
        var owner = new Window();
        var recorder = new ShortcutRecorderWindow();
        try
        {
            owner.Show();
            var selection = recorder.ShowDialog<string?>(owner);
            Send(recorder, true, Key.LeftCtrl, KeyModifiers.Control);
            Send(recorder, true, Key.G, KeyModifiers.Control);
            Send(recorder, false, modifierFirst ? Key.LeftCtrl : Key.G,
                modifierFirst ? KeyModifiers.None : KeyModifiers.Control);
            Assert.False(selection.IsCompleted);
            Send(recorder, false, modifierFirst ? Key.G : Key.LeftCtrl, KeyModifiers.None);
            Assert.Equal("Ctrl+G", await selection);
        }
        finally { recorder.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task Escape_cancels_without_a_candidate()
    {
        var owner = new Window();
        var recorder = new ShortcutRecorderWindow();
        try
        {
            owner.Show();
            var selection = recorder.ShowDialog<string?>(owner);
            Send(recorder, true, Key.Escape, KeyModifiers.None);
            Assert.Null(await selection);
        }
        finally { recorder.Close(); owner.Close(); }
    }

    private static void Send(Window window, bool down, Key key, KeyModifiers modifiers) =>
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = down ? InputElement.KeyDownEvent : InputElement.KeyUpEvent,
            Key = key, KeyModifiers = modifiers
        });
}

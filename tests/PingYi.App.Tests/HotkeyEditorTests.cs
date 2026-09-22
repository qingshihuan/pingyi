using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public sealed class HotkeyEditorTests
{
    // Tab content can be reached through both its item and content presenter.
    // Assert one distinct control instance, not one traversal path.
    private static Button EditorButton(SettingsWindow window, string name) =>
        window.GetLogicalDescendants().OfType<Button>().Distinct().Single(button => button.Name == name);

    [AvaloniaFact]
    public void Editor_has_accessible_controls_localizes_and_validates_input()
    {
        var window = new SettingsWindow();
        try
        {
            window.Show();
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 4;
            UiText.Configure("en-US");
            Assert.Equal("Record shortcut", EditorButton(window, "RecordHotkeyButton").Content);
            Assert.Equal("Restore default", EditorButton(window, "ResetHotkeyButton").Content);
            var field = window.FindControl<TextBox>("HotkeyBox")!;
            field.Text = "Ctrl++D";
            Assert.False(EditorButton(window, "ApplyHotkeyButton").IsEnabled);
            Assert.False(window.FindControl<Button>("SaveSettingsButton")!.IsEnabled);
            field.Text = "Ctrl+Shift+G";
            Assert.True(EditorButton(window, "ApplyHotkeyButton").IsEnabled);
            EditorButton(window, "ResetHotkeyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(AppSettings.DefaultHotkey, field.Text);
            UiText.Configure("zh-CN");
            Assert.Equal("录制快捷键", EditorButton(window, "RecordHotkeyButton").Content);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Cancel_recording_preserves_unsaved_input_and_reenables_save()
    {
        var window = new SettingsWindow();
        try
        {
            window.Show();
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 4;
            var field = window.FindControl<TextBox>("HotkeyBox")!;
            field.Text = "Ctrl+Alt+G";
            await window.BeginHotkeyRecordingAsync();
            Assert.True(field.IsReadOnly);
            Assert.False(window.FindControl<Button>("SaveSettingsButton")!.IsEnabled);
            await window.FinishHotkeyRecordingAsync(false);
            Assert.Equal("Ctrl+Alt+G", field.Text);
            Assert.False(field.IsReadOnly);
            Assert.True(window.FindControl<Button>("SaveSettingsButton")!.IsEnabled);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(Key.D, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+D")]
    [InlineData(Key.G, KeyModifiers.Control | KeyModifiers.Alt, "Ctrl+Alt+G")]
    [InlineData(Key.D9, KeyModifiers.Control, "Ctrl+9")]
    public void Recorder_formats_supported_keys(Key key, KeyModifiers modifiers, string expected)
    {
        Assert.True(SettingsWindow.TryRecordHotkey(key, modifiers, out var text));
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(Key.LeftCtrl, KeyModifiers.Control)]
    [InlineData(Key.F2, KeyModifiers.Control)]
    [InlineData(Key.D, KeyModifiers.Meta)]
    [InlineData(Key.D, KeyModifiers.None)]
    public void Recorder_rejects_modifier_only_or_unsupported_keys(Key key, KeyModifiers modifiers) =>
        Assert.False(SettingsWindow.TryRecordHotkey(key, modifiers, out _));

    [Fact]
    public async Task Recording_suspends_then_restores_once_even_after_repeated_cancel()
    {
        var fake = new RecordingHotkey();
        var session = new HotkeyRecordingSession(fake, "Ctrl+Shift+D");
        await session.BeginAsync();
        await session.BeginAsync();
        Assert.True(session.IsRecording);
        Assert.Equal(new[] { "stop" }, fake.Calls);
        await session.EndAsync();
        await session.EndAsync();
        Assert.False(session.IsRecording);
        Assert.Equal(new[] { "stop", "Ctrl+Shift+D" }, fake.Calls);
    }

    private sealed class RecordingHotkey : IGlobalHotkeyService
    {
        public List<string> Calls { get; } = [];
        public event EventHandler? Pressed { add { } remove { } }
        public Task StartAsync(string key, CancellationToken token = default) { Calls.Add(key); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken token = default) { Calls.Add("stop"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using PingYi.Core;
using PingYi.Infrastructure;
using Xunit;

namespace PingYi.App.Tests;

public class LinuxCaptureUiTests
{
    private static readonly ImageFrame Frame = new([1, 2, 3], 12, 8, new PixelRect(0, 0, 12, 8));

    [AvaloniaFact]
    public async Task System_selection_bypasses_desktop_capture_overlay_and_recropping()
    {
        var service = new Interactive(Frame);
        var cropper = new Cropper();
        var result = await CaptureSelectionWorkflow.SelectAsync(service, cropper, [],
            (_, _, _) => throw new Exception("Must not show a second selector"), CancellationToken.None);
        Assert.Same(Frame, result);
        Assert.Equal(0, cropper.Calls);
        Assert.Equal(1, service.Calls);
    }
    [AvaloniaFact]
    public async Task System_cancel_is_a_normal_empty_selection_and_restores_windows()
    {
        var window = new Window(); window.Show();
        try
        {
            var result = await CaptureWindowScope.RunAsync([window], _ => Task.CompletedTask,
                token => CaptureSelectionWorkflow.SelectAsync(new Interactive(null), new Cropper(), [],
                    (_, _, _) => throw new Exception("No overlay"), token), CancellationToken.None);
            Assert.Null(result);
            Assert.True(window.IsVisible);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task X11_still_uses_its_native_overlay_with_monitor_geometry()
    {
        var cropper = new Cropper(); var overlayCalls = 0;
        var display = new CaptureDisplay(new PixelRect(-1280, 0, 1280, 720), 1.5);
        var result = await CaptureSelectionWorkflow.SelectAsync(new Desktop(), cropper, [display],
            (_, monitors, _) => { overlayCalls++; Assert.Equal(display, Assert.Single(monitors)); return Task.FromResult<PixelRect?>(new(1, 2, 3, 4)); }, CancellationToken.None);
        Assert.Equal(1, overlayCalls); Assert.Equal(1, cropper.Calls); Assert.Same(Frame, result);
    }
    [AvaloniaTheory]
    [InlineData("zh-CN", false)]
    [InlineData("en-US", true)]
    public void Capture_failure_stays_visible_and_has_accessible_recovery(string language, bool dark)
    {
        UiText.Configure(language);
        var window = new CaptureErrorWindow(UiText.Error(new ProviderException("capture_portal_unavailable", "系统截图门户不可用。")), null)
            { Width = 400, Height = 280, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); window.UpdateLayout();
            Assert.True(window.IsVisible);
            var text = window.FindControl<TextBlock>("CaptureErrorText")!;
            Assert.False(string.IsNullOrEmpty(text.Text));
            Assert.True(text.Bounds.Width > 0);
            Snapshot(window, $"linux-capture-error-{language}");
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task Conflict_status_survives_model_ready_message_and_language_change()
    {
        UiText.Configure("en-US");
        await using var keys = new HotkeyRegistrationService(new Conflict());
        await Assert.ThrowsAsync<ProviderException>(() => keys.StartAsync("Ctrl+Alt+D"));
        Assert.Contains("in use", HotkeyFeedback.Message(keys));
        Assert.DoesNotContain("Ctrl+Alt+D", HotkeyFeedback.Badge(keys));
        UiText.Configure("zh-CN");
        Assert.Contains("占用", HotkeyFeedback.Message(keys));
    }
    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void Linux_guidance_has_readonly_real_executable_command(string language)
    {
        UiText.Configure(language);
        var window = new SettingsWindow { Width = 800, Height = 560 };
        try
        {
            window.Show(); window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 4; window.UpdateLayout();
            var command = window.FindControl<TextBox>("CaptureCommandBox")!;
            Assert.True(command.IsReadOnly);
            Assert.EndsWith(" --capture", command.Text);
            Assert.DoesNotContain("sudo", command.Text);
            Assert.Equal(AppSettings.DefaultHotkey, window.FindControl<TextBox>("HotkeyBox")!.PlaceholderText);
            Snapshot(window, $"linux-shortcut-settings-{language}");
        }
        finally { window.Close(); }
    }
    private static void Snapshot(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap); bitmap.Save(Path.Combine(directory, name + ".png"));
    }
    private sealed class Interactive(ImageFrame? frame) : IInteractiveScreenCaptureService
    {
        public int Calls;
        public Task<ImageFrame?> CaptureSelectionAsync(CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(frame); }
        public Task<ImageFrame> CaptureDesktopAsync(CancellationToken cancellationToken = default) => throw new Exception("No desktop API under Wayland");
    }
    private sealed class Desktop : IScreenCaptureService
    { public Task<ImageFrame> CaptureDesktopAsync(CancellationToken cancellationToken = default) => Task.FromResult(Frame); }
    private sealed class Cropper : IImageCropper
    { public int Calls; public ImageFrame Crop(ImageFrame source, PixelRect bounds) { Calls++; return Frame; } }
    private sealed class Conflict : IGlobalHotkeyService
    {
        public event EventHandler? Pressed { add { } remove { } }
        public Task StartAsync(string key, CancellationToken cancellationToken = default) => Task.FromException(new ProviderException("hotkey_conflict", "快捷键占用"));
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

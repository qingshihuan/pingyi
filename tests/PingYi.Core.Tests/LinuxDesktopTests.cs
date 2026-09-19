using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;

namespace PingYi.Core.Tests;

public class LinuxDesktopTests
{
    [Theory]
    [InlineData("wayland", "wayland-0", ":0", LinuxDesktopSession.Wayland)]
    [InlineData(null, "wayland-0", ":0", LinuxDesktopSession.Wayland)]
    [InlineData("x11", "stale", ":1", LinuxDesktopSession.X11)]
    [InlineData("x11", null, null, LinuxDesktopSession.Unavailable)]
    [InlineData(null, null, ":2", LinuxDesktopSession.X11)]
    [InlineData(null, null, null, LinuxDesktopSession.Unavailable)]
    public void Session_detection_does_not_treat_XWayland_as_a_full_X11_desktop(string? type, string? wayland, string? display, LinuxDesktopSession expected) =>
        Assert.Equal(expected, LinuxDesktop.Detect(type, wayland, display));

    [Theory]
    [InlineData(8, true, "Ctrl+Alt+D", "Ctrl+Alt+Shift+D")]
    [InlineData(8, true, " ctrl + alt + d ", "Ctrl+Alt+Shift+D")]
    [InlineData(8, true, "Ctrl+Alt+G", "Ctrl+Alt+G")]
    [InlineData(9, true, "Ctrl+Alt+D", "Ctrl+Alt+D")]
    [InlineData(8, false, "Ctrl+Alt+D", "Ctrl+Alt+D")]
    [InlineData(1, true, "Ctrl+Shift+X", "Ctrl+Alt+Shift+D")]
    [InlineData(1, false, "Ctrl+Shift+X", "Ctrl+Alt+D")]
    public void Only_the_old_Linux_default_migrates_once(int schema, bool linux, string shortcut, string expected)
    {
        var before = new AppSettings { SchemaVersion = schema, Hotkey = shortcut, CustomTranslationModel = "keep-model", UiLanguage = "en-US" };
        var after = before.Normalize(linux);
        Assert.Equal(expected, after.Hotkey);
        Assert.Equal("keep-model", after.CustomTranslationModel);
        Assert.Equal("en-US", after.UiLanguage);
        Assert.Equal(AppSettings.CurrentSchemaVersion, after.SchemaVersion);
        Assert.Equal(after, after.Normalize(linux));
    }

    [Fact]
    public async Task Registration_failure_is_not_ready_and_can_be_retried()
    {
        var native = new FakeKeys { Fail = true };
        await using var service = new HotkeyRegistrationService(native);
        var changes = 0;
        service.RegistrationChanged += (_, _) => changes++;
        await Assert.ThrowsAsync<ProviderException>(() => service.StartAsync("Ctrl+Alt+Shift+D"));
        Assert.False(service.IsRegistered);
        Assert.Null(service.RegisteredShortcut);
        Assert.IsType<ProviderException>(service.RegistrationError);
        native.Fail = false;
        await service.StartAsync("Ctrl+Alt+Shift+G");
        Assert.True(service.IsRegistered);
        Assert.Equal("Ctrl+Alt+Shift+G", service.RegisteredShortcut);
        Assert.Null(service.RegistrationError);
        await service.StopAsync();
        Assert.False(service.IsRegistered);
        Assert.True(changes >= 3);
    }

    [Fact]
    public async Task Stop_cancels_a_pending_permission_request_instead_of_deadlocking()
    {
        var native = new FakeKeys { Wait = true };
        await using var service = new HotkeyRegistrationService(native);
        var startup = service.StartAsync("Ctrl+Alt+Shift+D");
        await native.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup);
        Assert.False(service.IsRegistered);
    }

    [Fact]
    public void Portal_file_is_validated_and_not_deleted()
    {
        var path = Path.Combine(Path.GetTempPath(), "pingyi-synthetic-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using var bitmap = new SKBitmap(12, 8);
            bitmap.Erase(SKColors.White);
            using var image = SKImage.FromBitmap(bitmap);
            using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, bytes.ToArray());
            var frame = PortalScreenCaptureService.ReadPortalImage(new Uri(path).AbsoluteUri);
            Assert.Equal(12, frame.Width);
            Assert.Equal(8, frame.Height);
            Assert.Equal(new PixelRect(0, 0, 12, 8), frame.DesktopBounds);
            Assert.True(File.Exists(path));
            File.WriteAllText(path, "not a screenshot");
            Assert.Equal("capture_portal_image", Assert.Throws<ProviderException>(() => PortalScreenCaptureService.ReadPortalImage(new Uri(path).AbsoluteUri)).Code);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("https://example.com/image.png")]
    [InlineData("file://example.com/image.png")]
    [InlineData("file:///tmp/not-a-file.png?secret=test")]
    [InlineData("relative.png")]
    public void Portal_will_not_fetch_a_remote_or_ambiguous_image(string uri) =>
        Assert.Equal("capture_portal_image", Assert.Throws<ProviderException>(() => PortalScreenCaptureService.ReadPortalImage(uri)).Code);

    private sealed class FakeKeys : IGlobalHotkeyService
    {
        public bool Fail, Wait;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler? Pressed { add { } remove { } }
        public async Task StartAsync(string shortcut, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (Fail) throw new ProviderException("hotkey_conflict", "test conflict");
            if (Wait) await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

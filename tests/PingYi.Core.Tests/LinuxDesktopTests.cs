using PingYi.Infrastructure;
using System.Diagnostics;

namespace PingYi.Core.Tests;

public class LinuxDesktopTests
{
    [Theory]
    [InlineData("wayland", null, true)]
    [InlineData("Wayland", "wayland-0", true)]
    [InlineData(null, "wayland-1", true)]
    [InlineData("x11", "stale", false)]
    [InlineData(null, null, false)]
    public void Session_detection_does_not_confuse_XWayland_with_X11(string? type, string? display, bool expected) =>
        Assert.Equal(expected, LinuxDesktopSession.IsWaylandSession(type, display));

    [Theory]
    [InlineData(true, 8, "Ctrl+Alt+D", "Ctrl+Shift+D")]
    [InlineData(false, 8, "Ctrl+Alt+D", "Ctrl+Alt+D")]
    [InlineData(true, 8, "Ctrl+Alt+G", "Ctrl+Alt+G")]
    [InlineData(true, 9, "Ctrl+Alt+D", "Ctrl+Alt+D")]
    public void Old_default_is_migrated_only_on_Linux(bool linux, int schema, string before, string after)
    {
        var settings = new AppSettings { SchemaVersion = schema, Hotkey = before, UiLanguage = "en-US", CustomTranslationModel = "keep-model" };
        var normalized = settings.NormalizeForPlatform(linux);
        Assert.Equal(after, normalized.Hotkey);
        Assert.Equal("keep-model", normalized.CustomTranslationModel);
        Assert.Equal("en-US", normalized.UiLanguage);
    }

    [Fact]
    public void Portal_refuses_remote_files_without_accessing_network() =>
        Assert.Equal("linux_capture_invalid", Assert.Throws<ProviderException>(() =>
            PortalScreenCaptureService.ReadPortalImage("https://example.com/private.png", default)).Code);

    [Fact]
    public async Task X11_conflict_does_not_exit_the_process_and_capture_still_works()
    {
        if (Environment.GetEnvironmentVariable("PINGYI_LINUX_NATIVE_TESTS") != "1") return;
        await using var first = new X11GlobalHotkeyService();
        await using var second = new X11GlobalHotkeyService();
        await first.StartAsync(AppSettings.LinuxDefaultHotkey);
        await first.StartAsync(AppSettings.LinuxDefaultHotkey); // idempotent, no orphan thread
        var error = await Assert.ThrowsAsync<ProviderException>(() => second.StartAsync(AppSettings.LinuxDefaultHotkey));
        Assert.Equal("hotkey_conflict", error.Code);
        // The old implementation can exit on BadAccess before reaching this assertion.
        var frame = await new X11ScreenCaptureService().CaptureDesktopAsync();
        Assert.True(frame.Width > 0 && frame.Height > 0);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, frame.PngBytes[..4]);
        await first.StopAsync();
        var pressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        second.Pressed += (_, _) => pressed.TrySetResult();
        await second.StartAsync(AppSettings.LinuxDefaultHotkey);
        using var xdotool = Process.Start(new ProcessStartInfo("xdotool")
        {
            ArgumentList = { "key", "--clearmodifiers", AppSettings.LinuxDefaultHotkey.ToLowerInvariant() }, UseShellExecute = false
        })!;
        await xdotool.WaitForExitAsync();
        await pressed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await second.StopAsync();
        await second.StartAsync("Ctrl+Alt+Shift+G");
    }

    [Fact]
    public async Task Native_portal_uses_one_connection_catches_early_response_cancellation_and_close()
    {
        if (Environment.GetEnvironmentVariable("PINGYI_LINUX_NATIVE_TESTS") != "1") return;
        var portal = new PortalScreenCaptureService();
        var image = await portal.CaptureSelectionAsync(); // Fixture emits response BEFORE method returns.
        Assert.NotNull(image);
        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Null(await portal.CaptureSelectionAsync()); // User clicks Cancel.
        var error = await Assert.ThrowsAsync<ProviderException>(() => portal.CaptureSelectionAsync());
        Assert.Equal("linux_portal_failed", error.Code);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => portal.CaptureSelectionAsync(cancel.Token));
        var marker = Environment.GetEnvironmentVariable("PINGYI_PORTAL_CLOSED")!;
        Assert.True(File.Exists(marker), "Cancel must send Request.Close to the same live connection.");
        Assert.NotNull(await portal.CaptureSelectionAsync()); // Recovery after cancellation.
    }
}

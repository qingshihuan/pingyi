using System.Diagnostics;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

// Same xUnit class serializes all synthetic keyboard injection on the shared Xvfb display.
public partial class LinuxDesktopTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_change_restores_an_actually_triggerable_X11_binding(bool failPersistence)
    {
        if (Environment.GetEnvironmentVariable("PINGYI_LINUX_NATIVE_TESTS") != "1") return;
        const string previous = "Ctrl+Alt+Shift+J", next = "Ctrl+Alt+Shift+K";
        await using var active = new X11GlobalHotkeyService();
        await using var blocker = new X11GlobalHotkeyService();
        await active.StartAsync(previous);
        if (!failPersistence) await blocker.StartAsync(next);
        var saved = false;
        Exception? registrationError = null;
        await Assert.ThrowsAnyAsync<Exception>(() => HotkeyBindingChange.ApplyAsync(active, previous, next, () =>
        {
            if (failPersistence) throw new IOException("Synthetic persistence failure");
            saved = true;
            return Task.CompletedTask;
        }, error => registrationError = error));
        Assert.False(saved);
        Assert.Null(registrationError);
        await AssertTriggered(active, previous);
    }

    [Fact]
    public async Task Recording_releases_the_real_grab_and_cancel_restores_it()
    {
        if (Environment.GetEnvironmentVariable("PINGYI_LINUX_NATIVE_TESTS") != "1") return;
        const string shortcut = "Ctrl+Alt+Shift+L";
        await using var active = new X11GlobalHotkeyService();
        await active.StartAsync(shortcut);
        var result = await HotkeyBindingChange.RecordAsync(active, shortcut, async () =>
        {
            await using var probe = new X11GlobalHotkeyService();
            await probe.StartAsync(shortcut); // Would throw BadAccess if recording hadn't released it.
            await probe.StopAsync();
            return null;
        }, error => Assert.Null(error));
        Assert.Null(result);
        await AssertTriggered(active, shortcut);
    }

    private static async Task AssertTriggered(IGlobalHotkeyService service, string shortcut)
    {
        var pressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPressed(object? sender, EventArgs args) => pressed.TrySetResult();
        service.Pressed += OnPressed;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("xdotool")
            {
                UseShellExecute = false,
                ArgumentList = { "key", "--clearmodifiers", shortcut.ToLowerInvariant() }
            })!;
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            await pressed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { service.Pressed -= OnPressed; }
    }
}

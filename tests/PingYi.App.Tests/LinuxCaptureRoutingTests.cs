using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public class LinuxCaptureRoutingTests
{
    [Fact]
    public async Task Portal_selection_is_not_recaptured_or_mapped_to_X11_monitor_coordinates()
    {
        var service = new Portal();
        bool overlayCalled = false;
        var image = await CaptureSelectionRouter.SelectAsync(service, (_, _) =>
        { overlayCalled = true; throw new Exception("Must not overlay a portal-selected image"); }, default);
        Assert.Same(service.Image, image);
        Assert.False(overlayCalled);
        service.Cancel = true;
        Assert.Null(await CaptureSelectionRouter.SelectAsync(service, (_, _) => throw new Exception(), default));
    }
    [Fact]
    public async Task X11_capture_still_enters_the_application_selection_interface()
    {
        var service = new Desktop();
        int calls = 0;
        var image = await CaptureSelectionRouter.SelectAsync(service, (frame, _) =>
        { calls++; return Task.FromResult<ImageFrame?>(frame); }, default);
        Assert.Equal(1, calls);
        Assert.NotNull(image);
    }
    [AvaloniaFact]
    public void Linux_settings_explain_system_binding_and_offer_a_cold_start_capture_command()
    {
        var window = new SettingsWindow();
        try
        {
            window.Show();
            var command = window.FindControl<TextBox>("LinuxCaptureCommandBox")!;
            Assert.EndsWith(" --capture", command.Text);
            Assert.True(command.IsReadOnly);
            UiText.Configure("en-US");
            Assert.Equal("Copy capture command", window.FindControl<Button>("CopyLinuxCaptureCommandButton")!.Content);
        }
        finally { window.Close(); }
    }
    private sealed class Portal : IInteractiveScreenCaptureService
    {
        public ImageFrame Image { get; } = new([1], 2, 2, new(0, 0, 2, 2));
        public bool Cancel { get; set; }
        public Task<ImageFrame?> CaptureSelectionAsync(CancellationToken token = default) => Task.FromResult(Cancel ? null : Image);
        public Task<ImageFrame> CaptureDesktopAsync(CancellationToken token = default) => throw new Exception("Wrong capture path");
    }
    private sealed class Desktop : IScreenCaptureService
    {
        public Task<ImageFrame> CaptureDesktopAsync(CancellationToken token = default) =>
            Task.FromResult(new ImageFrame([1], 2, 2, new(0, 0, 2, 2)));
    }
}

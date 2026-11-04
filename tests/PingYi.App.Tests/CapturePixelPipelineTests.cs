using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;
using Xunit;

namespace PingYi.App.Tests;

public sealed class CapturePixelPipelineTests
{
    [AvaloniaFact]
    public void Bitmap_owns_a_copy_of_sliced_pixels_and_keeps_color_and_opaque_alpha()
    {
        var bytes = new byte[4 * 3 * 4];
        for (int i = 0; i < bytes.Length; i += 4) { bytes[i] = 30; bytes[i + 1] = 60; bytes[i + 2] = 90; }
        var view = new DesktopFrame(bytes, 16, new(-2, 1, 4, 3)).Slice(new(1, 1, 2, 2));
        using var bitmap = CaptureOverlayWindow.CreateBitmap(view);
        // Deliberately violate producer immutability after the copy to detect dangling references.
        Array.Fill<byte>(bytes, 0);
        GC.Collect();
        using var stream = new MemoryStream();
        bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        using var decoded = SKBitmap.Decode(stream.ToArray());
        Assert.Equal(2, decoded.Width); Assert.Equal(2, decoded.Height);
        Assert.All(decoded.Pixels, pixel => Assert.Equal(new SKColor(90, 60, 30, 255), pixel));
    }

    [AvaloniaFact]
    public async Task Esc_closes_all_displays_detaches_and_disposes_bitmaps()
    {
        var session = Session();
        var selected = session.ShowAndSelectAsync();
        Assert.Equal(3, session.Windows.Count);
        Assert.All(session.Windows, window => Assert.True(window.IsVisible));
        session.Windows[0].KeyPress(Key.Escape, default, PhysicalKey.Escape, null);
        Assert.Null(await selected);
        Dispatcher.UIThread.RunJobs();
        Assert.All(session.Windows, window =>
        {
            Assert.False(window.IsVisible);
            Assert.True(window.IsBitmapDisposed);
            Assert.Null(window.FindControl<CaptureScreenshotImage>("ScreenshotImage")!.Source);
        });
        session.Cancel(); // Cleanup is idempotent.
    }

    [AvaloniaFact]
    public async Task Rapid_cancel_and_reopen_uses_new_pixels_without_leaving_windows()
    {
        for (byte value = 1; value <= 12; value++)
        {
            using var cancellation = new CancellationTokenSource();
            var session = Session(value);
            var selected = session.ShowAndSelectAsync(cancellation.Token);
            var source = (Avalonia.Media.Imaging.Bitmap)session.Windows[0].FindControl<CaptureScreenshotImage>("ScreenshotImage")!.Source!;
            using var stream = new MemoryStream(); source.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            using var decoded = SKBitmap.Decode(stream.ToArray());
            Assert.Equal(value, decoded.GetPixel(0, 0).Blue);
            cancellation.Cancel();
            Dispatcher.UIThread.RunJobs();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => selected);
            Dispatcher.UIThread.RunJobs();
            Assert.All(session.Windows, window => Assert.True(window.IsBitmapDisposed && !window.IsVisible));
        }
    }

    [AvaloniaFact]
    public async Task Cross_monitor_selection_keeps_desktop_pixel_coordinates_with_different_requested_scales()
    {
        var session = Session();
        var selected = session.ShowAndSelectAsync();
        var owner = session.Windows[0];
        Assert.True(session.Begin(owner, new PixelPoint(-30, 12)));
        session.Move(owner, new PixelPoint(100, 70));
        session.End(owner, new PixelPoint(100, 70));
        Assert.Equal(new PingYi.Core.PixelRect(30, 12, 130, 58), await selected);
        Dispatcher.UIThread.RunJobs();
        Assert.All(session.Windows, window => Assert.True(window.IsBitmapDisposed));
    }

    [AvaloniaFact]
    public async Task Closing_one_monitor_and_cancel_before_render_resolve_session_and_readiness()
    {
        var session = Session();
        var selected = session.ShowAndSelectAsync();
        session.Windows[0].CloseOverlay();
        session.Cancel();
        Assert.Null(await selected);
        Assert.True(session.FirstFrame.Task.IsCanceled);
        Dispatcher.UIThread.RunJobs();
        Assert.All(session.Windows, window => Assert.True(window.IsBitmapDisposed));
    }

    [AvaloniaFact]
    public void Warmup_never_shows_a_window_or_keeps_a_screenshot()
    {
        CaptureOverlayWarmup.Run();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task Router_prefers_raw_capture_and_does_not_request_a_full_desktop_png()
    {
        var service = new RawCapture();
        await CaptureSelectionRouter.SelectAsync(service, (frame, _) =>
        {
            Assert.True(frame.IsRaw); Assert.Equal(0, frame.PngByteCount);
            return Task.FromResult<ImageFrame?>(null);
        }, TestContext.Current.CancellationToken);
        Assert.Equal(1, service.Calls);
    }

    private static CaptureOverlaySession Session(byte blue = 25)
    {
        var bytes = new byte[180 * 90 * 4];
        for (int i = 0; i < bytes.Length; i += 4) bytes[i] = blue;
        return new(new DesktopFrame(bytes, 180 * 4, new(-60, 0, 180, 90)),
            [new(new(-60, 0, 60, 60), 1), new(new(0, 0, 60, 90), 1.5), new(new(60, 0, 60, 60), 2)], new SkiaImageCropper());
    }

    private sealed class RawCapture : IDesktopFrameCaptureService
    {
        public int Calls;
        public Task<DesktopFrame> CaptureFrameAsync(CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new DesktopFrame(new byte[16], 8, new(0, 0, 2, 2))); }
        public Task<ImageFrame> CaptureDesktopAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("PNG path used");
    }
}

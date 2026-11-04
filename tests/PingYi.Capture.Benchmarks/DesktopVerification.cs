using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using PingYi.App;
using PingYi.Core;
using PingYi.Infrastructure;
using SkiaSharp;

internal static class DesktopVerification
{
    public static async Task RunAsync(Window shell)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var displays = CaptureDisplay.From(shell);
        var backdrops = new List<Window>();
        var covers = new List<Window>();
        var points = new List<PixelPoint>();
        var service = (IDesktopFrameCaptureService)ScreenCaptureServiceFactory.Create();
        var cropper = new SkiaImageCropper();
        try
        {
            foreach (var display in displays)
            {
                var x = display.Bounds.X + 100;
                var y = display.Bounds.Y + 100;
                var backdrop = Create(x, y, 160, Colors.Blue);
                var cover = Create(x + 20, y + 20, 80, Colors.Red);
                cover.ShowActivated = true; // Main/result windows restore with normal activation semantics.
                backdrops.Add(backdrop); covers.Add(cover);
                backdrop.Show(); cover.Show();
                points.Add(cover.PointToScreen(new Point(20, 20)));
            }
            await RenderAsync(covers[0], token);
            await DesktopCaptureBarrier.WaitAsync(token);
            var covered = await Task.Run(() => service.CaptureFrameAsync(token), token);
            foreach (var point in points) CheckPixel(covered, point, Colors.Red, "cover_visible");

            foreach (var color in new[] { Colors.Lime, Colors.Blue })
            {
                foreach (var backdrop in backdrops) backdrop.Background = new SolidColorBrush(color);
                await RenderAsync(backdrops[0], token);
                await CaptureWindowScope.RunAsync(covers, DesktopCaptureBarrier.WaitAsync, async ct =>
                {
                    if (covers.Any(window => window.IsVisible)) throw new DesktopVerificationException("hide");
                    var frame = await Task.Run(() => service.CaptureFrameAsync(ct), ct);
                    foreach (var point in points)
                    {
                        CheckPixel(frame, point, color, "fresh_background"); // Fresh underlying content; no own-window residue.
                        var selected = cropper.Crop(frame, new(point.X - frame.DesktopBounds.X, point.Y - frame.DesktopBounds.Y, 8, 8));
                        using var decoded = SKBitmap.Decode(selected.PngBytes);
                        if (decoded.GetPixel(0, 0).Red != color.R || decoded.GetPixel(0, 0).Blue != color.B)
                            throw new DesktopVerificationException("selection_png");
                    }
                    var session = new CaptureOverlaySession(frame, displays, cropper);
                    try
                    {
                        var pending = session.ShowAndSelectAsync(ct);
                        await session.FirstFrame.Task.WaitAsync(ct);
                        foreach (var window in session.Windows)
                        {
                            var expected = window.CaptureBounds;
                            if (window.Position != new PixelPoint(expected.X, expected.Y) ||
                                Math.Abs(window.ClientSize.Width * window.RenderScaling - expected.Width) > 1 ||
                                Math.Abs(window.ClientSize.Height * window.RenderScaling - expected.Height) > 1)
                                throw new DesktopVerificationException("native_dpi_geometry");
                        }
                        session.Cancel();
                        await pending;
                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                        if (session.Windows.Any(window => !window.IsBitmapDisposed || window.IsVisible))
                            throw new DesktopVerificationException("bitmap_cleanup");
                    }
                    finally { session.Cancel(); }
                    return true;
                }, token);
                if (covers.Any(window => !window.IsVisible)) throw new DesktopVerificationException("restore");
                await RenderAsync(covers[0], token);
                await DesktopCaptureBarrier.WaitAsync(token);
                var restored = await Task.Run(() => service.CaptureFrameAsync(token), token);
                foreach (var point in points) CheckPixel(restored, point, Colors.Red, "cover_restored");
            }
            Console.WriteLine(JsonSerializer.Serialize(new { verification = "passed", monitors = displays.Count,
                freshFrame = true, hideRestore = true, nativeDpiGeometry = true, selectedPng = true, bitmapCleanup = true }));
        }
        finally
        {
            foreach (var window in covers.Concat(backdrops)) window.Close();
        }
    }

    private static Window Create(int x, int y, int size, Color color) => new()
    {
        Title = "PingYi synthetic capture check", Position = new PixelPoint(x, y), Width = size, Height = size,
        Background = new SolidColorBrush(color), WindowDecorations = WindowDecorations.None,
        ShowInTaskbar = false, Topmost = true, ShowActivated = false
    };

    private static async Task RenderAsync(Window window, CancellationToken token)
    {
        window.InvalidateVisual();
        window.UpdateLayout();
        var compositor = ElementComposition.GetElementVisual(window)?.Compositor
            ?? throw new DesktopVerificationException("no_compositor");
        await compositor.RequestCompositionBatchCommitAsync().Rendered.WaitAsync(token);
    }

    private static void CheckPixel(DesktopFrame frame, PixelPoint point, Color expected, string check)
    {
        var offset = (point.Y - frame.DesktopBounds.Y) * frame.Stride + (point.X - frame.DesktopBounds.X) * 4;
        var pixel = frame.BgraPixels.Span.Slice(offset, 4);
        if (Math.Abs(pixel[0] - expected.B) > 3 || Math.Abs(pixel[1] - expected.G) > 3 || Math.Abs(pixel[2] - expected.R) > 3)
            throw new DesktopVerificationException(check);
    }
}

internal sealed class DesktopVerificationException(string check) : Exception
{
    public string Check { get; } = check;
}

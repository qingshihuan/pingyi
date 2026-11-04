using PingYi.Infrastructure;
using SkiaSharp;

namespace PingYi.Core.Tests;

public sealed class DesktopFrameTests
{
    [Fact]
    public void Raw_crop_preserves_top_down_bgra_channels_stride_and_negative_origin()
    {
        // Four pixels per row plus padding, opaque despite GDI's undefined alpha byte.
        var bytes = new byte[3 * 24];
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 4; x++)
        {
            var offset = y * 24 + x * 4;
            bytes[offset] = (byte)(10 + x); bytes[offset + 1] = (byte)(20 + y); bytes[offset + 2] = (byte)(30 + x + y);
        }
        var desktop = new DesktopFrame(bytes, 24, new(-4, -2, 4, 3));
        var view = desktop.Slice(new(1, 1, 2, 2));
        Assert.Equal(24, view.Stride);
        Assert.Equal(new PixelRect(-3, -1, 2, 2), view.DesktopBounds);
        Assert.True(view.BgraPixels.Span.SequenceEqual(bytes.AsSpan(28, 32)));
        var cropped = new SkiaImageCropper().Crop(desktop, new(1, 1, 2, 2));
        using var bitmap = SKBitmap.Decode(cropped.PngBytes);
        Assert.Equal(new SKColor(32, 21, 11, 255), bitmap.GetPixel(0, 0));
        Assert.Equal(new SKColor(34, 22, 12, 255), bitmap.GetPixel(1, 1));
        Assert.Equal(view.DesktopBounds, cropped.DesktopBounds);
    }

    [Fact]
    public void Raw_views_reject_incomplete_buffers_bad_stride_and_out_of_bounds_slices()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DesktopFrame(new byte[64], 7, new(0, 0, 2, 2)));
        Assert.Throws<ArgumentException>(() => new DesktopFrame(new byte[15], 8, new(0, 0, 2, 2)));
        Assert.Throws<OverflowException>(() => new DesktopFrame(new byte[1], int.MaxValue, new(0, 0, int.MaxValue, 2)));
        var frame = new DesktopFrame(new byte[64], 16, new(0, 0, 4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.Slice(new(-1, 0, 2, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.Slice(new(3, 0, 2, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.Slice(new(0, 0, 0, 2)));
    }

    [Fact]
    public void Raw_and_encoded_crop_have_identical_pixels_and_bounds()
    {
        var bytes = Enumerable.Range(0, 80).Select(i => (byte)(i * 3)).ToArray();
        var raw = new DesktopFrame(bytes, 20, new(-9, 11, 5, 4));
        var cropper = new SkiaImageCropper();
        var png = cropper.Crop(raw, new(0, 0, 5, 4));
        var first = cropper.Crop(raw, new(2, 1, 3, 3));
        var second = cropper.Crop(new DesktopFrame(png), new(2, 1, 3, 3));
        using var a = SKBitmap.Decode(first.PngBytes);
        using var b = SKBitmap.Decode(second.PngBytes);
        Assert.Equal(a.Pixels, b.Pixels);
        Assert.Equal(first.DesktopBounds, second.DesktopBounds);
    }

    [Fact]
    public async Task Timing_context_flows_to_workers_but_does_not_leak_between_operations()
    {
        var timing = new CaptureTiming();
        var samples = new List<CaptureTimingSample>();
        timing.Sampled += samples.Add;
        using (timing.Enter())
        {
            timing.SetDisplays(3); timing.SetFrame(7680, 2160, 0);
            await Task.Run(() => { Assert.Same(timing, CaptureTiming.Current); using (timing.Measure("gdi_bitblt")) { } });
        }
        Assert.Null(CaptureTiming.Current);
        var sample = Assert.Single(samples);
        Assert.Equal(3, sample.Displays); Assert.Equal(7680, sample.Width); Assert.Equal(0, sample.PngBytes);
        Assert.True(sample.TotalMs >= sample.DurationMs && sample.DurationMs >= 0);
    }
}

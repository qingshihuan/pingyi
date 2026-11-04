using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PingYi.Core;

namespace PingYi.App;

internal static class CaptureOverlayWarmup
{
    /// <summary>Warm XAML, fonts and Skia using synthetic pixels. Never show a window or capture a desktop.</summary>
    internal static void Run()
    {
        Dispatcher.UIThread.VerifyAccess();
        var frame = new DesktopFrame(new byte[] { 24, 24, 24, 255 }, 4, new(0, 0, 1, 1));
        var window = new CaptureOverlayWindow(frame, 1, null) { Width = 320, Height = 180 };
        try
        {
            window.Measure(new Size(320, 180));
            window.Arrange(new Rect(0, 0, 320, 180));
            using var bitmap = new RenderTargetBitmap(new PixelSize(320, 180));
            bitmap.Render(window);
        }
        finally { window.CloseOverlay(); }
    }
}

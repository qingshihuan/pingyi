using PingYi.Core;

namespace PingYi.App;

internal static class CaptureSelectionRouter
{
    public static async Task<ImageFrame?> SelectAsync(IScreenCaptureService service,
        Func<DesktopFrame, CancellationToken, Task<ImageFrame?>> showOverlay, CancellationToken cancellationToken)
    {
        // Portal interaction can take minutes. Never put it under the 15-second pixel-read timeout,
        // or map its cropped image onto XWayland's unrelated screen bounds.
        if (service is IInteractiveScreenCaptureService interactive)
            return await interactive.CaptureSelectionAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        DesktopFrame desktop;
        try
        {
            using (CaptureTiming.Current?.Measure("desktop_capture_worker"))
                desktop = await Task.Run(async () => service is IDesktopFrameCaptureService raw
                    ? await raw.CaptureFrameAsync(timeout.Token)
                    : new DesktopFrame(await service.CaptureDesktopAsync(timeout.Token)), timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ProviderException("capture_timeout", "屏幕捕获超时，请重试。"); }
        cancellationToken.ThrowIfCancellationRequested();
        CaptureTiming.Current?.SetFrame(desktop.Width, desktop.Height, desktop.PngByteCount);
        return await showOverlay(desktop, cancellationToken);
    }
}

using PingYi.Core;

namespace PingYi.App;

internal static class CaptureSelectionRouter
{
    public static async Task<ImageFrame?> SelectAsync(IScreenCaptureService service,
        Func<ImageFrame, CancellationToken, Task<ImageFrame?>> showOverlay, CancellationToken cancellationToken)
    {
        // Portal interaction can take minutes. Never put it under the 15-second pixel-read timeout,
        // or map its cropped image onto XWayland's unrelated screen bounds.
        if (service is IInteractiveScreenCaptureService interactive)
            return await interactive.CaptureSelectionAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        ImageFrame desktop;
        try { desktop = await Task.Run(() => service.CaptureDesktopAsync(timeout.Token), timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ProviderException("capture_timeout", "屏幕捕获超时，请重试。"); }
        cancellationToken.ThrowIfCancellationRequested();
        return await showOverlay(desktop, cancellationToken);
    }
}

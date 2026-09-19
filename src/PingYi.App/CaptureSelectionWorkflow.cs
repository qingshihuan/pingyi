using PingYi.Core;

namespace PingYi.App;

internal static class CaptureSelectionWorkflow
{
    internal static async Task<ImageFrame?> SelectAsync(IScreenCaptureService service, IImageCropper cropper,
        IReadOnlyList<CaptureDisplay>? displays,
        Func<ImageFrame, IReadOnlyList<CaptureDisplay>, CancellationToken, Task<PixelRect?>> selectOverlay,
        CancellationToken token)
    {
        // The portal's interaction is not a desktop read. It has its own user-facing
        // deadline and owns region selection; never apply the 15-second X11 timeout.
        if (service is IInteractiveScreenCaptureService interactive)
            return await interactive.CaptureSelectionAsync(token);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        ImageFrame desktop;
        try { desktop = await service.CaptureDesktopAsync(deadline.Token); }
        catch (OperationCanceledException e) when (!token.IsCancellationRequested)
        { throw new ProviderException("capture_timeout", "屏幕捕获超时，请重试。", e); }
        token.ThrowIfCancellationRequested();
        var monitors = displays is { Count: > 0 } ? displays : new[] { new CaptureDisplay(desktop.DesktopBounds, 1) };
        var selection = await selectOverlay(desktop, monitors, token);
        token.ThrowIfCancellationRequested();
        return selection is null ? null : cropper.Crop(desktop, selection.Value);
    }
}

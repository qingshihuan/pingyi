namespace PingYi.Core;

/// <summary>A compositor-owned selection; its returned pixels must not be cropped using X11 coordinates.</summary>
public interface IInteractiveScreenCaptureService : IScreenCaptureService
{
    Task<ImageFrame?> CaptureSelectionAsync(CancellationToken cancellationToken = default);
}

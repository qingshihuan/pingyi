namespace PingYi.Core;

public interface IOcrProvider
{
    ProviderMetadata Metadata { get; }
    ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<OcrResult> RecognizeAsync(
        ImageFrame image,
        OcrOptions options,
        CancellationToken cancellationToken = default);
}

public interface ITranslationProvider
{
    ProviderMetadata Metadata { get; }
    ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IScreenCaptureService
{
    Task<ImageFrame> CaptureDesktopAsync(CancellationToken cancellationToken = default);
}

/// <summary>Optional fast desktop path; portals and existing encoded services are unchanged.</summary>
public interface IDesktopFrameCaptureService : IScreenCaptureService
{
    Task<DesktopFrame> CaptureFrameAsync(CancellationToken cancellationToken = default);
}

public interface IImageCropper
{
    ImageFrame Crop(ImageFrame source, PixelRect cropBounds);
    ImageFrame Crop(DesktopFrame source, PixelRect cropBounds) =>
        Crop(source.EncodedImage ?? throw new NotSupportedException("Raw frame cropping is not supported."), cropBounds);
}

public interface IGlobalHotkeyService : IAsyncDisposable
{
    event EventHandler? Pressed;
    Task StartAsync(string shortcut, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface ISecretStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

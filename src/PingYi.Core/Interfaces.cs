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

// The compositor owns selection/permissions on Wayland. Its returned image is
// already selected; it must never be treated as a root-window desktop bitmap.
public interface IInteractiveScreenCaptureService : IScreenCaptureService
{
    Task<ImageFrame?> CaptureSelectionAsync(CancellationToken cancellationToken = default);
}

public interface IGlobalHotkeyStatus
{
    bool IsRegistered { get; }
    string? RegisteredShortcut { get; }
    Exception? RegistrationError { get; }
    event EventHandler? RegistrationChanged;
}

public interface IImageCropper
{
    ImageFrame Crop(ImageFrame source, PixelRect cropBounds);
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

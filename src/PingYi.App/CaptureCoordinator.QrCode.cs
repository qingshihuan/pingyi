using PingYi.Core;

namespace PingYi.App;

public sealed partial class CaptureCoordinator
{
    private async Task ProcessQrCodeAsync(OperationContext operation, ResultWindow window, ImageFrame image)
    {
        EnsureCurrent(operation);
        window.SetLoading(UiText.Get("String.QrLoading"), UiText.Get("String.QrPrivacy"));
        try
        {
            var results = _probeCache.TryGetValue(image, out var probe) && probe.Qr is { } cached
                ? cached : await services.QrCodeDecoder.DecodeAsync(image, operation.Token);
            EnsureCurrent(operation);
            window.SetQrResults(results);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            EnsureCurrent(operation);
            window.SetQrFailure(); // Payloads and decoder exception details never enter diagnostics.
        }
    }
}

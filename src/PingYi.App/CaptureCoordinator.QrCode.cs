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
            var results = await services.QrCodeDecoder.DecodeAsync(image, operation.Token);
            EnsureCurrent(operation);
            window.SetQrResults(results);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            EnsureCurrent(operation);
            // Decoded payloads and codec exception details must never enter diagnostics.
            window.SetQrFailure();
        }
    }
}

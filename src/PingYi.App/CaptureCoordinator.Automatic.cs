using PingYi.Core;

namespace PingYi.App;

public sealed partial class CaptureCoordinator
{
    private async Task ProcessAutomaticAsync(OperationContext operation, ResultWindow window, ImageFrame image)
    {
        EnsureCurrent(operation);
        window.SetLoading(CaptureUiText.Preparing, CaptureUiText.LocalProbePrivacy);
        IReadOnlyList<QrCodeResult> codes = [];
        try
        {
            codes = await WithTimeoutAsync(token => services.QrCodeDecoder.DecodeAsync(image, token),
                TimeSpan.FromSeconds(8), operation.Token, "auto_qr_timeout", "二维码探测超时。");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { throw; }
        catch (Exception) { } // Decoder failure never sends content elsewhere.
        EnsureCurrent(operation);
        var decision = AutomaticCapture.Decide(codes.Count);
        window.SetAutomaticDecision(decision);
        window.SetPurpose(decision.Purpose!.Value);
        await ProcessAsync(operation, window, image);
    }
}

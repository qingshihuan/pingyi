namespace PingYi.Core;

public enum CaptureDecisionReason { DecodedQr, UseCurrentModel }
public sealed record CaptureDecision(CapturePurpose? Purpose, CaptureDecisionReason Reason, int QrCount)
{
    public bool RequiresChoice => Purpose is null;
}

public static class AutomaticCapture
{
    // QR is decoded locally. All other content uses the current model; remote consent
    // is requested by the capture coordinator before sending any image or text.
    public static CaptureDecision Decide(int qrCount) => qrCount < 0
        ? throw new ArgumentOutOfRangeException(nameof(qrCount))
        : qrCount > 0 ? new(CapturePurpose.DecodeQrCode, CaptureDecisionReason.DecodedQr, qrCount)
        : new(CapturePurpose.TranslateText, CaptureDecisionReason.UseCurrentModel, 0);
}

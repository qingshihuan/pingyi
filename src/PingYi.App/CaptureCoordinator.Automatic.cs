using System.Runtime.CompilerServices;
using PingYi.Core;

namespace PingYi.App;

public sealed partial class CaptureCoordinator
{
    // Keys disappear when their screenshot session ends. Never write images/results to disk.
    private readonly ConditionalWeakTable<ImageFrame, LocalCaptureEvidence> _probeCache = new();

    private sealed class LocalCaptureEvidence
    {
        public IReadOnlyList<QrCodeResult>? Qr { get; set; }
        public OcrResult? Ocr { get; set; }
        public string? SourceLanguage { get; set; }
    }

    private bool TryGetPaddleProbe(ImageFrame image, string source, out OcrResult result)
    {
        result = null!;
        if (!_probeCache.TryGetValue(image, out var probe) || probe.SourceLanguage != source || probe.Ocr is null) return false;
        result = probe.Ocr;
        return true;
    }

    private async Task ProcessAutomaticAsync(OperationContext operation, ResultWindow window, ImageFrame image)
    {
        EnsureCurrent(operation);
        window.SetLoading(CaptureUiText.Preparing, CaptureUiText.LocalProbePrivacy);
        var probe = _probeCache.GetOrCreateValue(image);
        var source = services.Settings.SourceLanguage;
        if (probe.Qr is null)
        {
            try
            {
                probe.Qr = await WithTimeoutAsync(token => services.QrCodeDecoder.DecodeAsync(image, token),
                    TimeSpan.FromSeconds(8), operation.Token, "auto_qr_timeout", "二维码探测超时。");
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { throw; }
            catch (Exception) { /* Probe failure is not absence of a code; manual decoding remains available. */ }
        }
        EnsureCurrent(operation);
        if (probe.Ocr is null || probe.SourceLanguage != source)
        {
            probe.Ocr = null;
            probe.SourceLanguage = source;
            try
            {
                var availability = await WithTimeoutAsync(token => services.PaddleProvider.GetAvailabilityAsync(token).AsTask(),
                    TimeSpan.FromSeconds(8), operation.Token, "auto_ocr_timeout", "本地文字探测超时。");
                EnsureCurrent(operation);
                if (availability.IsAvailable)
                    probe.Ocr = await WithTimeoutAsync(token => services.PaddleProvider.RecognizeAsync(image, new OcrOptions(source), token),
                        TimeSpan.FromSeconds(20), operation.Token, "auto_ocr_timeout", "本地文字探测超时。");
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { throw; }
            catch (Exception) { /* No error content, recognized text or payload is logged. */ }
        }
        EnsureCurrent(operation);
        var decision = probe.Qr is null
            ? new CaptureDecision(null, CaptureDecisionReason.ProbeUnavailable, 0)
            : AutomaticCapture.Decide(image.Width, image.Height, probe.Qr.Count, probe.Ocr, probe.Ocr is not null);
        window.SetAutomaticDecision(decision);
        if (decision.RequiresChoice)
        {
            window.SetAnalysisCancelled(CaptureUiText.ChooseTask);
            return;
        }
        window.SetPurpose(decision.Purpose!.Value);
        await ProcessAsync(operation, window, image);
    }
}

namespace PingYi.Core;

public enum CaptureDecisionReason
{
    DecodedQr, ReadableText, NoReadableText, MixedContent, UncertainText, ProbeUnavailable
}

/// <summary>A recommendation from local evidence, never a claim to know the user's intent.</summary>
public sealed record CaptureDecision(CapturePurpose? Purpose, CaptureDecisionReason Reason, int QrCount)
{
    public bool RequiresChoice => Purpose is null;
}

public static class AutomaticCapture
{
    /// <summary>
    /// Pure routing policy. Callers supply local QR/Paddle evidence only, not a configurable
    /// cloud provider. Scores are heuristics, not calibrated probabilities. Explicit manual
    /// purposes bypass this policy. No translation, downloads or network access happen here.
    /// </summary>
    public static CaptureDecision Decide(int width, int height, int qrCount, OcrResult? ocr, bool ocrCompleted)
    {
        if (width <= 0 || height <= 0 || qrCount < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!ocrCompleted || ocr is null)
            return qrCount > 0
                ? new(CapturePurpose.DecodeQrCode, CaptureDecisionReason.DecodedQr, qrCount)
                : new(null, CaptureDecisionReason.ProbeUnavailable, 0);

        var candidates = ocr.Blocks.Where(block => !string.IsNullOrWhiteSpace(block.Text)).ToArray();
        var readable = candidates.Where(block => double.IsFinite(block.Confidence) && block.Confidence >= 0.70).ToArray();
        var letters = readable.Sum(block => block.Text.Count(char.IsLetter));
        // Clamp to the image; OCR boxes must not create negative or unbounded coverage.
        var area = readable.Sum(block =>
        {
            var rect = block.Bounds;
            var left = Math.Clamp((long)rect.X, 0, width);
            var top = Math.Clamp((long)rect.Y, 0, height);
            var right = Math.Clamp((long)rect.X + Math.Max(0, rect.Width), 0, width);
            var bottom = Math.Clamp((long)rect.Y + Math.Max(0, rect.Height), 0, height);
            return (double)Math.Max(0, right - left) * Math.Max(0, bottom - top);
        });
        var coverage = Math.Min(1, area / ((double)width * height));
        var substantialText = letters >= 12 || (letters >= 2 && coverage >= 0.12);

        if (qrCount > 0)
            return substantialText
                ? new(null, CaptureDecisionReason.MixedContent, qrCount)
                : new(CapturePurpose.DecodeQrCode, CaptureDecisionReason.DecodedQr, qrCount);
        if (substantialText)
            return new(CapturePurpose.TranslateText, CaptureDecisionReason.ReadableText, 0);
        // An OCR error, low confidence, numbers/URLs or a few fragments is not proof of a photo.
        if (candidates.Length > 0 || !string.IsNullOrWhiteSpace(ocr.PlainText))
            return new(null, CaptureDecisionReason.UncertainText, 0);
        return new(CapturePurpose.DescribeImage, CaptureDecisionReason.NoReadableText, 0);
    }
}

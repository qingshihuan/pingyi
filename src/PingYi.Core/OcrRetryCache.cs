using System.Runtime.CompilerServices;

namespace PingYi.Core;

/// <summary>Session-only OCR reuse after translation failure; never persist user content.</summary>
public sealed class OcrRetryCache
{
    private readonly ConditionalWeakTable<ImageFrame, Entry> _entries = new();
    private sealed class Entry(AppSettings settings, OcrResult result, string providerLabel)
    {
        public string Key { get; } = Route(settings);
        public OcrResult Result { get; } = result;
        public string Label { get; } = providerLabel;
        public bool TranslationPending { get; set; } = true;
    }
    private static string Route(AppSettings settings) => string.Join('|', settings.OcrProviderId,
        settings.SourceLanguage, settings.OcrProviderId == "local-vlm-ocr" ? settings.CustomTranslationEndpoint : "",
        settings.OcrProviderId == "local-vlm-ocr" ? settings.CustomTranslationModel : "");
    public void Remember(ImageFrame image, AppSettings settings, OcrResult result, string label)
    {
        _entries.Remove(image); _entries.Add(image, new Entry(settings, result, label));
    }
    public bool TryGetForTranslationRetry(ImageFrame image, AppSettings settings, out OcrResult result, out string label)
    {
        result = null!; label = "";
        if (!_entries.TryGetValue(image, out var entry) || !entry.TranslationPending || entry.Key != Route(settings)) return false;
        result = entry.Result; label = entry.Label; return true;
    }
    public void MarkTranslationComplete(ImageFrame image)
    {
        if (_entries.TryGetValue(image, out var entry)) entry.TranslationPending = false;
    }
}

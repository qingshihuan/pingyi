namespace PingYi.Core;

/// <summary>Stable routing IDs separated from presentation and from unverified quality claims.</summary>
public static class ProcessingModes
{
    public const string Offline = "offline";
    public const string LocalTranslation = "local-translation";
    public const string VisionCorrection = "vision-correction";
    public const string Baidu = "baidu";
    public const string Google = "google";
    public const string Custom = "custom";

    public static string Identify(AppSettings settings)
    {
        if (settings.OcrProviderId == "local-paddle" && settings.TranslationProviderId == "local-argos")
            return Offline;
        if (settings.TranslationProviderId == "custom-chat" &&
            AppSettings.TryParseChatCompletionsEndpoint(settings.CustomTranslationEndpoint, out var endpoint) && endpoint.IsLoopback)
        {
            if (settings.OcrProviderId == "local-paddle") return LocalTranslation;
            if (settings.OcrProviderId == "local-vlm-corrected") return VisionCorrection;
        }
        if (settings.OcrProviderId == "baidu-ocr" && settings.TranslationProviderId == "baidu-translate") return Baidu;
        if (settings.OcrProviderId == "google-vision-ocr" && settings.TranslationProviderId == "google-translate") return Google;
        return Custom;
    }

    public static AppSettings Apply(AppSettings settings, string mode)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var updated = mode switch
        {
            Offline => settings with
            {
                OcrProviderId = "local-paddle", TranslationProviderId = "local-argos",
                TargetLanguage = settings.TargetLanguage is "zh" or "en" or "auto-opposite"
                    ? settings.TargetLanguage : LanguageCatalog.AutoOpposite
            },
            LocalTranslation => LocalLlmPresets.ApplyLocalMode(settings, "local-paddle"),
            VisionCorrection => LocalLlmPresets.ApplyLocalMode(settings, "local-vlm-corrected"),
            Baidu => settings with { OcrProviderId = "baidu-ocr", TranslationProviderId = "baidu-translate" },
            Google => settings with { OcrProviderId = "google-vision-ocr", TranslationProviderId = "google-translate" },
            Custom => settings,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown processing mode.")
        };
        // RuntimePolicy stops owned processes when no active provider uses them. Keep the
        // downloaded model configuration so switching back can reuse it without reconfiguration.
        return updated.Normalize();
    }
}

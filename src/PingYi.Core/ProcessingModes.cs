namespace PingYi.Core;

/// <summary>Exact provider combinations. Display copy belongs to the app's language resources.</summary>
public sealed record ProcessingMode(string Id, string? OcrProviderId, string? TranslationProviderId);

public static class ProcessingModes
{
    public static IReadOnlyList<ProcessingMode> All { get; } =
    [
        new("offline", "local-paddle", "local-argos"),
        new("llm", "local-paddle", "custom-chat"),
        new("vision", "local-vlm-corrected", "custom-chat"),
        new("baidu", "baidu-ocr", "baidu-translate"),
        new("custom", null, null)
    ];

    public static ProcessingMode Match(AppSettings settings) => All.FirstOrDefault(mode =>
        mode.OcrProviderId == settings.OcrProviderId &&
        mode.TranslationProviderId == settings.TranslationProviderId) ?? All[^1];

    public static AppSettings Apply(AppSettings settings, string modeId)
    {
        var mode = All.FirstOrDefault(mode => mode.Id == modeId)
            ?? throw new ArgumentException("Unknown processing mode.", nameof(modeId));
        if (mode.Id == "custom") return settings; // Navigation only; never overwrite custom routing.
        return settings with
        {
            OcrProviderId = mode.OcrProviderId!,
            TranslationProviderId = mode.TranslationProviderId!
            // Preserve endpoint/model, managed-runtime settings, target language and credentials.
            // The app adjusts a target only when the selected provider does not support it.
        };
    }
}

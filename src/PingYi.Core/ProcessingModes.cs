namespace PingYi.Core;

/// <summary>Provider combinations; task selection (auto/translate/describe/QR) is independent.</summary>
public sealed record ProcessingMode(string Id, string? OcrProviderId, string? TranslationProviderId);

public static class ProcessingModes
{
    public static IReadOnlyList<ProcessingMode> All { get; } =
    [
        new("basic", "local-vlm-ocr", "custom-chat"),
        new("lite", "local-paddle", "local-argos"),
        new("llm", "local-paddle", "custom-chat"),
        new("baidu", "baidu-ocr", "baidu-translate"),
        new("custom", null, null)
    ];

    public static ProcessingMode Match(AppSettings settings)
    {
        // A remotely configured endpoint is a custom combination, not a local basic mode.
        var match = All.FirstOrDefault(mode => mode.OcrProviderId == settings.OcrProviderId &&
            mode.TranslationProviderId == settings.TranslationProviderId);
        if (match?.Id == "basic" && !IsLocalEndpoint(settings)) return All[^1];
        return match ?? All[^1];
    }

    public static bool IsLocalEndpoint(AppSettings settings) =>
        AppSettings.TryParseChatCompletionsEndpoint(settings.CustomTranslationEndpoint, out var endpoint) &&
        endpoint.IsLoopback && string.IsNullOrEmpty(endpoint.UserInfo);

    public static AppSettings Apply(AppSettings settings, string modeId)
    {
        // Keep old offline deep links working; the removed visual-correction mode has no alias.
        if (modeId == "offline") modeId = "lite";
        var mode = All.FirstOrDefault(mode => mode.Id == modeId)
            ?? throw new ArgumentException("Unknown processing mode.", nameof(modeId));
        if (mode.Id == "custom") return settings;
        if (mode.Id == "basic" && !IsLocalEndpoint(settings))
            throw new ProviderException("basic_requires_local", "基础模式需要本机回环模型端点，请先配置本机视觉模型；远程服务请使用自定义组合。");
        return settings with { OcrProviderId = mode.OcrProviderId!, TranslationProviderId = mode.TranslationProviderId! };
    }
}

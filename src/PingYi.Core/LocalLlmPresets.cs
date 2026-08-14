namespace PingYi.Core;

public sealed record LocalLlmPreset(
    string Id,
    string DisplayName,
    string ChatCompletionsEndpoint,
    string SuggestedModel = "")
{
    public string LocalizedDisplayName =>
        !string.Equals(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase) &&
        Id == "vllm"
            ? "vLLM / other compatible service"
            : DisplayName;

    public override string ToString() => LocalizedDisplayName;
}

public static class LocalLlmPresets
{
    public static IReadOnlyList<LocalLlmPreset> All { get; } =
    [
        new(
            "llama-cpp",
            "llama.cpp",
            AppSettings.DefaultCustomTranslationEndpoint,
            AppSettings.DefaultCustomTranslationModel),
        new(
            "ollama",
            "Ollama",
            "http://127.0.0.1:11434/v1/chat/completions"),
        new(
            "lm-studio",
            "LM Studio",
            "http://127.0.0.1:1234/v1/chat/completions"),
        new(
            "vllm",
            "vLLM / 其他兼容服务",
            "http://127.0.0.1:8000/v1/chat/completions")
    ];

    public static LocalLlmPreset Default => All[0];

    public static AppSettings ApplyLocalMode(AppSettings settings, string ocrProviderId)
    {
        var preset = Default;
        var hasUsableLocalConfiguration =
            AppSettings.TryParseChatCompletionsEndpoint(settings.CustomTranslationEndpoint, out var configuredEndpoint) &&
            configuredEndpoint.IsLoopback &&
            !string.IsNullOrWhiteSpace(settings.CustomTranslationModel);
        return settings with
        {
            OcrProviderId = ocrProviderId,
            TranslationProviderId = "custom-chat",
            CustomTranslationEndpoint = hasUsableLocalConfiguration
                ? configuredEndpoint.AbsoluteUri.TrimEnd('/')
                : preset.ChatCompletionsEndpoint,
            CustomTranslationModel = hasUsableLocalConfiguration
                ? settings.CustomTranslationModel.Trim()
                : preset.SuggestedModel,
            ManagedRuntimeEnabled = hasUsableLocalConfiguration && settings.ManagedRuntimeEnabled
        };
    }

    public static LocalLlmPreset? MatchEndpoint(string? endpoint)
    {
        var normalized = AppSettings.NormalizeChatCompletionsEndpoint(endpoint);
        return All.FirstOrDefault(preset =>
            string.Equals(preset.ChatCompletionsEndpoint, normalized, StringComparison.OrdinalIgnoreCase));
    }
}

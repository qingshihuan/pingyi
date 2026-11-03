namespace PingYi.Core;

/// <summary>
/// Well-known OpenAI Chat Completions-compatible third-party endpoints.
/// Model IDs and credentials are intentionally supplied by the user.
/// </summary>
public sealed record ThirdPartyApiPreset(string Id, string DisplayName, string ChatCompletionsEndpoint)
{
    public override string ToString() => DisplayName;
}

public static class ThirdPartyApiPresets
{
    public static IReadOnlyList<ThirdPartyApiPreset> All { get; } =
    [
        new("openai", "OpenAI", "https://api.openai.com/v1/chat/completions"),
        new("deepseek", "DeepSeek", "https://api.deepseek.com/chat/completions"),
        new("siliconflow", "SiliconFlow", "https://api.siliconflow.cn/v1/chat/completions"),
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1/chat/completions"),
        new("dashscope", "阿里云百炼 / DashScope", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions"),
        new("zhipu", "智谱 BigModel", "https://open.bigmodel.cn/api/paas/v4/chat/completions")
    ];

    public static ThirdPartyApiPreset? MatchEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) ||
            !AppSettings.TryParseChatCompletionsEndpoint(endpoint, out var parsed))
            return null;
        var normalized = parsed.AbsoluteUri.TrimEnd('/');
        return All.FirstOrDefault(preset =>
            string.Equals(preset.ChatCompletionsEndpoint, normalized, StringComparison.OrdinalIgnoreCase));
    }
}

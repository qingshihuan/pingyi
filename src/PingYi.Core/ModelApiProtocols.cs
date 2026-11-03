namespace PingYi.Core;

public static class ModelApiProtocols
{
    public const string ChatCompletions = "chat-completions";
    public const string Responses = "responses";

    public static string Normalize(string? value) =>
        string.Equals(value, Responses, StringComparison.OrdinalIgnoreCase)
            ? Responses
            : ChatCompletions;

    public static bool IsOfficialOpenAiEndpoint(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase);
}

public sealed record ApiProtocolChoice(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public static class ApiProtocolChoices
{
    public static IReadOnlyList<ApiProtocolChoice> All { get; } =
    [
        new(ModelApiProtocols.Responses, "OpenAI Responses API"),
        new(ModelApiProtocols.ChatCompletions, "Chat Completions")
    ];

    public static ApiProtocolChoice Get(string? id) =>
        All.First(choice => choice.Id == ModelApiProtocols.Normalize(id));
}

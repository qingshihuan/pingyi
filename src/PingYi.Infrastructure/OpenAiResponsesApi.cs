using System.Text.Json;
using PingYi.Core;

namespace PingYi.Infrastructure;

internal static class OpenAiResponsesApi
{
    public static Uri ResolveEndpoint(Uri chatCompletionsEndpoint, string? protocol)
    {
        if (ModelApiProtocols.Normalize(protocol) != ModelApiProtocols.Responses)
            return chatCompletionsEndpoint;

        const string chatSuffix = "/chat/completions";
        var path = chatCompletionsEndpoint.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(chatSuffix, StringComparison.OrdinalIgnoreCase))
            path = path[..^chatSuffix.Length] + "/responses";
        else if (path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            return chatCompletionsEndpoint;
        else
            path = path.TrimEnd('/') + "/responses";

        return new UriBuilder(chatCompletionsEndpoint)
        {
            Path = path,
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
    }

    public static string ReadOutputText(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Responses API result must be an object.");

        if (root.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(outputText.GetString()))
            return outputText.GetString()!;

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            throw new JsonException("Responses API result has no output items.");

        var parts = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("type", out var itemType) ||
                itemType.ValueKind != JsonValueKind.String ||
                !string.Equals(itemType.GetString(), "message", StringComparison.Ordinal))
                continue;

            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object ||
                    !part.TryGetProperty("type", out var partType) ||
                    partType.ValueKind != JsonValueKind.String ||
                    !string.Equals(partType.GetString(), "output_text", StringComparison.Ordinal) ||
                    !part.TryGetProperty("text", out var text) ||
                    text.ValueKind != JsonValueKind.String)
                    continue;
                parts.Add(text.GetString()!);
            }
        }

        var result = string.Join("\n", parts).Trim();
        if (result.Length == 0)
            throw new JsonException("Responses API result contains no output text.");
        return result;
    }

    public static JsonObject CreateImageInput(string prompt, byte[] pngBytes) =>
        new()
        {
            ["role"] = "user",
            ["content"] = new JsonArray(
                new JsonObject { ["type"] = "input_text", ["text"] = prompt },
                new JsonObject
                {
                    ["type"] = "input_image",
                    ["image_url"] = "data:image/png;base64," + Convert.ToBase64String(pngBytes),
                    ["detail"] = "high"
                })
        };
}

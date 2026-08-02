using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PingYi.Core;

namespace PingYi.Infrastructure;

public sealed class ChatCompatibleTranslationProvider(
    HttpClient httpClient,
    ISecretStore secretStore,
    Func<AppSettings> settingsAccessor) : ITranslationProvider
{
    public ProviderMetadata Metadata { get; } = new(
        "custom-chat",
        "本地 / 自定义大模型",
        ProviderExecutionLocation.Configurable,
        UploadsImage: false,
        RequiresSecret: false,
        LanguageCatalog.Codes);

    public async ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var settings = settingsAccessor();
        if (!Uri.TryCreate(settings.CustomTranslationEndpoint, UriKind.Absolute, out var endpoint) ||
            !string.IsNullOrWhiteSpace(endpoint.Query) ||
            string.IsNullOrWhiteSpace(settings.CustomTranslationModel))
        {
            return new ProviderAvailability(false, "请填写 OpenAI 兼容接口地址和模型名。");
        }

        if (!endpoint.IsLoopback)
        {
            return ProviderAvailability.Available;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var modelIds = await QueryAvailableModelsAsync(endpoint, timeout.Token);
            if (modelIds.Length > 0 && !modelIds.Contains(settings.CustomTranslationModel, StringComparer.OrdinalIgnoreCase))
            {
                return new ProviderAvailability(
                    false,
                    $"服务已连接，但模型名“{settings.CustomTranslationModel}”不存在；当前可用：{string.Join("、", modelIds.Take(3))}。");
            }

            return ProviderAvailability.Available;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProviderAvailability(false, "连接本地大模型超时；请确认所选服务已启动。");
        }
        catch (HttpRequestException)
        {
            return new ProviderAvailability(false, "无法连接本地大模型；请确认 llama.cpp、Ollama、LM Studio 或兼容服务已启动，并检查端口。");
        }
        catch (JsonException)
        {
            return new ProviderAvailability(false, "本地模型列表响应格式不正确；请确认服务兼容 OpenAI API。");
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = settingsAccessor();
        if (!Uri.TryCreate(
                AppSettings.NormalizeChatCompletionsEndpoint(settings.CustomTranslationEndpoint),
                UriKind.Absolute,
                out var endpoint))
        {
            throw new ProviderException("custom_endpoint_invalid", "OpenAI 兼容接口地址无效。");
        }

        return await QueryAvailableModelsAsync(endpoint, cancellationToken);
    }

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = settingsAccessor();
        if (!Uri.TryCreate(AppSettings.NormalizeChatCompletionsEndpoint(settings.CustomTranslationEndpoint), UriKind.Absolute, out var endpoint) ||
            string.IsNullOrWhiteSpace(settings.CustomTranslationModel))
        {
            throw new ProviderException("custom_endpoint_invalid", "自定义翻译接口配置不完整。");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        var apiKey = await secretStore.GetAsync(SecretKeys.CustomTranslationApiKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        if (!LanguageCatalog.IsKnown(request.TargetLanguage))
        {
            throw new ProviderException(
                "custom_target_language_unsupported",
                $"尚未配置有效的目标语言：{request.TargetLanguage}。");
        }

        var sourceInstruction = request.SourceLanguage == LanguageCatalog.Auto
            ? "自动识别输入语言"
            : $"识别输入为{LanguageCatalog.GetPromptName(request.SourceLanguage)}";
        var targetName = LanguageCatalog.GetPromptName(request.TargetLanguage);
        var messages = new JsonArray();
        messages.Add((JsonNode)new JsonObject
        {
            ["role"] = "system",
            ["content"] = $"你是专业翻译引擎。{sourceInstruction}，翻译为{targetName}。保留原意、专有名词、段落和换行，只输出译文，不解释。"
        });
        messages.Add((JsonNode)new JsonObject
        {
            ["role"] = "user",
            ["content"] = request.Text
        });
        var payload = new JsonObject
        {
            ["model"] = settings.CustomTranslationModel,
            ["temperature"] = 0,
            ["messages"] = messages
        };
        message.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var responsePayload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderException("custom_translate_http", $"自定义翻译接口返回 HTTP {(int)response.StatusCode}。");
        }

        using var document = JsonDocument.Parse(responsePayload);
        try
        {
            var text = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
            return new TranslationResult(text.Trim(), request.SourceLanguage, request.TargetLanguage);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            throw new ProviderException("custom_translate_schema", "自定义接口响应不符合兼容格式。", exception);
        }
    }

    private static Uri BuildModelsEndpoint(Uri chatEndpoint)
    {
        var path = chatEndpoint.AbsolutePath.TrimEnd('/');
        const string chatSuffix = "/chat/completions";
        if (path.EndsWith(chatSuffix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^chatSuffix.Length] + "/models";
        }
        else
        {
            path = "/v1/models";
        }

        return new UriBuilder(chatEndpoint)
        {
            Path = path,
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
    }

    private async Task<string[]> QueryAvailableModelsAsync(
        Uri chatEndpoint,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildModelsEndpoint(chatEndpoint));
        var apiKey = await secretStore.GetAsync(SecretKeys.CustomTranslationApiKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"本地大模型服务返回 HTTP {(int)response.StatusCode}；请检查服务地址。",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("data", out var data)
            ? data.EnumerateArray()
                .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToArray()
            : [];
    }
}

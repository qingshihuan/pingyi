using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PingYi.Core;
using SkiaSharp;

namespace PingYi.Infrastructure;

/// <summary>A separate image_url request: no OCR precondition, translation, or silent fallback.</summary>
public sealed class ChatCompatibleImageAnalysisProvider(
    HttpClient httpClient, ISecretStore secretStore, AppSettings settings) : IImageAnalysisProvider
{
    public static TimeSpan RequestTimeout { get; } = TimeSpan.FromMinutes(3);
    public const int MaximumImageEdge = 1600;
    public const int MaximumResponseBytes = 256 * 1024;

    public async Task<ImageAnalysisResult> AnalyzeAsync(ImageFrame image, ImageAnalysisOptions options,
        CancellationToken cancellationToken = default)
    {
        var prompt = ImageAnalysisPrompts.Build(options);
        if (!AppSettings.TryParseChatCompletionsEndpoint(settings.CustomTranslationEndpoint, out var endpoint) ||
            string.IsNullOrWhiteSpace(settings.CustomTranslationModel) || !string.IsNullOrEmpty(endpoint.UserInfo))
            throw new ProviderException("image_analysis_configuration", "请在自定义接口中配置支持图片的模型，或在完全版中安装本机多模态模型。");
        if (!AppSettings.IsChatCompletionsTransportAllowed(endpoint))
            throw new ProviderException("custom_endpoint_insecure_transport", "远程自定义服务必须使用 HTTPS；只有本机回环地址可以使用 HTTP。");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestTimeout);
        try
        {
            var png = await Task.Run(() => PrepareImage(image, deadline.Token), deadline.Token);
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
            var key = await secretStore.GetAsync(SecretKeys.CustomTranslationApiKey, deadline.Token);
            if (!string.IsNullOrWhiteSpace(key)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            var payload = new JsonObject
            {
                ["model"] = settings.CustomTranslationModel, ["temperature"] = 0.2, ["max_tokens"] = 1536, ["stream"] = false,
                ["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = ImageAnalysisPrompts.System },
                    new JsonObject { ["role"] = "user", ["content"] = new JsonArray(
                        new JsonObject { ["type"] = "text", ["text"] = prompt },
                        new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject
                            { ["url"] = "data:image/png;base64," + Convert.ToBase64String(png), ["detail"] = "high" } }) })
            };
            message.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new ProviderException("image_analysis_http", $"图片分析接口返回 HTTP {(int)response.StatusCode}。请确认服务支持 image_url、模型具备视觉能力并已加载视觉组件；没有退回文字 OCR。");
            var body = await ReadBoundedAsync(response.Content, deadline.Token);
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("choices", out var choices) ||
                    choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 ||
                    choices[0].ValueKind != JsonValueKind.Object || !choices[0].TryGetProperty("message", out var reply) ||
                    reply.ValueKind != JsonValueKind.Object || !reply.TryGetProperty("content", out var content))
                    throw new JsonException();
                var text = ReadText(content).Trim();
                if (text.Length == 0)
                    throw new ProviderException("image_analysis_empty", "视觉模型没有返回有效内容；请检查模型的图片支持并重试。");
                if (choices[0].TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String &&
                    reason.GetString() == "length")
                    text += options.OutputLanguage == "en-US"
                        ? "\n\n[Output reached the model's token limit; this result may be incomplete.]"
                        : "\n\n[输出达到模型长度上限，以上内容可能不完整。]";
                return new ImageAnalysisResult(text, options.Purpose, settings.CustomTranslationModel);
            }
            catch (JsonException) { throw new ProviderException("image_analysis_schema", "图片分析响应不符合兼容格式。"); }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException("image_analysis_timeout", "图片分析超时。可缩小截图，选择更小的视觉模型或检查运行后端后重试。");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            // Never expose remote response bodies, request data or credentials in diagnostics.
            throw new ProviderException("image_analysis_connection", "无法连接图片分析服务。请确认模型服务已启动后重试。");
        }
    }

    private static string ReadText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? string.Empty;
        if (content.ValueKind != JsonValueKind.Array) throw new JsonException();
        var parts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("text", out var text) ||
                text.ValueKind != JsonValueKind.String) throw new JsonException();
            parts.Add(text.GetString()!);
        }
        return string.Join("\n", parts);
    }

    private static byte[] PrepareImage(ImageFrame image, CancellationToken token)
    {
        if (image.Width <= 0 || image.Height <= 0 || image.PngBytes.Length is 0 or > 32 * 1024 * 1024)
            throw InvalidImage();
        using var data = SKData.CreateCopy(image.PngBytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
            (long)codec.Info.Width * codec.Info.Height > 32_000_000) throw InvalidImage();
        token.ThrowIfCancellationRequested();
        using var bitmap = SKBitmap.Decode(image.PngBytes);
        if (bitmap is null) throw InvalidImage();
        var scale = Math.Min(1d, MaximumImageEdge / (double)Math.Max(bitmap.Width, bitmap.Height));
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(bitmap.Width * scale)),
            Math.Max(1, (int)Math.Round(bitmap.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var resized = new SKBitmap(info);
        using (var canvas = new SKCanvas(resized))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(bitmap, new SKRect(0, 0, info.Width, info.Height));
        }
        token.ThrowIfCancellationRequested();
        using var encoded = resized.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null || encoded.Size > 12 * 1024 * 1024) throw InvalidImage();
        return encoded.ToArray();
    }

    private static ProviderException InvalidImage() => new("image_analysis_image_invalid", "图片为空、损坏或过大。请重新框选较小的区域。");

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken token)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
            throw new ProviderException("image_analysis_response_large", "图片分析返回内容过大，已停止读取。");
        await using var stream = await content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes)
                throw new ProviderException("image_analysis_response_large", "图片分析返回内容过大，已停止读取。");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}

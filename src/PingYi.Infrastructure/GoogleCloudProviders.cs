using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PingYi.Core;

namespace PingYi.Infrastructure;

public sealed class GoogleCloudVisionOcrProvider(HttpClient httpClient, ISecretStore secretStore) : IOcrProvider
{
    private static readonly Uri Endpoint = new("https://vision.googleapis.com/v1/images:annotate");

    public ProviderMetadata Metadata { get; } = new(
        "google-vision-ocr",
        "Google Cloud Vision OCR",
        ProviderExecutionLocation.Cloud,
        UploadsImage: true,
        RequiresSecret: true,
        LanguageCatalog.Codes);

    public async ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = await secretStore.GetAsync(SecretKeys.GoogleCloudApiKey, cancellationToken);
        return string.IsNullOrWhiteSpace(apiKey)
            ? new ProviderAvailability(false, "Please configure your Google Cloud API key first.")
            : ProviderAvailability.Available;
    }

    public async Task<OcrResult> RecognizeAsync(
        ImageFrame image,
        OcrOptions options,
        CancellationToken cancellationToken = default)
    {
        if (image.PngBytes.Length == 0 || image.Width <= 0 || image.Height <= 0)
        {
            throw new ProviderException("google_vision_image_invalid", "Google Cloud Vision received an empty image.");
        }

        using var document = await AnnotateAsync(image.PngBytes, options.SourceLanguage, cancellationToken);
        var response = ReadFirstResponse(document.RootElement);
        ThrowIfApiError(response);

        var fullText = response.TryGetProperty("fullTextAnnotation", out var fullTextAnnotation) &&
                       fullTextAnnotation.TryGetProperty("text", out var textValue)
            ? textValue.GetString()?.Trim() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(fullText) &&
            response.TryGetProperty("textAnnotations", out var fallbackAnnotations) &&
            fallbackAnnotations.GetArrayLength() > 0)
        {
            fullText = fallbackAnnotations[0].TryGetProperty("description", out var description)
                ? description.GetString()?.Trim() ?? string.Empty
                : string.Empty;
        }

        if (string.IsNullOrWhiteSpace(fullText))
        {
            throw new ProviderException("no_text", "Google Cloud Vision did not find text in the selected area.");
        }

        var blocks = ReadBlocks(response, image.Width, image.Height);
        if (blocks.Count == 0)
        {
            blocks.Add(new OcrBlock(fullText, new PixelRect(0, 0, image.Width, image.Height), 0.9));
        }

        var detectedLanguage = ReadDetectedLanguage(response);
        return new OcrResult(
            blocks,
            fullText,
            string.IsNullOrWhiteSpace(detectedLanguage)
                ? TextProcessing.DetectLanguage(fullText)
                : detectedLanguage);
    }

    public async Task ValidateCredentialsAsync(CancellationToken cancellationToken = default)
    {
        // A fixed transparent PNG verifies the user's key without uploading user content.
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+X9s3AAAAAElFTkSuQmCC");
        using var document = await AnnotateAsync(png, LanguageCatalog.Auto, cancellationToken);
        ThrowIfApiError(ReadFirstResponse(document.RootElement));
    }

    private async Task<JsonDocument> AnnotateAsync(
        byte[] pngBytes,
        string sourceLanguage,
        CancellationToken cancellationToken)
    {
        var apiKey = await secretStore.GetAsync(SecretKeys.GoogleCloudApiKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ProviderException("credentials_missing", "Google Cloud API key is not configured.");
        }

        var requestItem = new JsonObject
        {
            ["image"] = new JsonObject { ["content"] = Convert.ToBase64String(pngBytes) },
            ["features"] = new JsonArray(new JsonObject
            {
                ["type"] = "TEXT_DETECTION",
                ["maxResults"] = 1
            })
        };
        if (sourceLanguage != LanguageCatalog.Auto)
        {
            requestItem["imageContext"] = new JsonObject
            {
                ["languageHints"] = new JsonArray(ToGoogleLanguageCode(sourceLanguage))
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        request.Content = new StringContent(
            new JsonObject { ["requests"] = new JsonArray(requestItem) }.ToJsonString(),
            Encoding.UTF8,
            "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException exception)
        {
            throw new ProviderException(
                "google_vision_schema",
                $"Google Cloud Vision returned HTTP {(int)response.StatusCode} with an invalid response.",
                exception);
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = ReadTopLevelError(document.RootElement) ?? response.ReasonPhrase ?? "Request failed";
            document.Dispose();
            throw new ProviderException(
                "google_vision_http",
                $"Google Cloud Vision returned HTTP {(int)response.StatusCode}: {message}");
        }

        return document;
    }

    private static JsonElement ReadFirstResponse(JsonElement root)
    {
        if (!root.TryGetProperty("responses", out var responses) ||
            responses.ValueKind != JsonValueKind.Array ||
            responses.GetArrayLength() == 0)
        {
            throw new ProviderException("google_vision_schema", "Google Cloud Vision response is missing results.");
        }

        return responses[0];
    }

    private static void ThrowIfApiError(JsonElement response)
    {
        if (!response.TryGetProperty("error", out var error))
        {
            return;
        }

        var code = error.TryGetProperty("code", out var codeElement) ? codeElement.GetInt32() : 0;
        var message = error.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? "Unknown API error"
            : "Unknown API error";
        throw new ProviderException("google_vision_api", $"Google Cloud Vision error {code}: {message}");
    }

    private static string? ReadTopLevelError(JsonElement root) =>
        root.TryGetProperty("error", out var error) &&
        error.TryGetProperty("message", out var message)
            ? message.GetString()
            : null;

    private static List<OcrBlock> ReadBlocks(JsonElement response, int imageWidth, int imageHeight)
    {
        var blocks = new List<OcrBlock>();
        if (!response.TryGetProperty("textAnnotations", out var annotations) ||
            annotations.ValueKind != JsonValueKind.Array)
        {
            return blocks;
        }

        foreach (var annotation in annotations.EnumerateArray().Skip(1))
        {
            var text = annotation.TryGetProperty("description", out var description)
                ? description.GetString()?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var bounds = annotation.TryGetProperty("boundingPoly", out var polygon)
                ? ReadBounds(polygon, imageWidth, imageHeight)
                : new PixelRect(0, blocks.Count * 24, imageWidth, 24);
            blocks.Add(new OcrBlock(text, bounds, 0.9));
        }

        return blocks;
    }

    private static PixelRect ReadBounds(JsonElement polygon, int imageWidth, int imageHeight)
    {
        if (!polygon.TryGetProperty("vertices", out var vertices) ||
            vertices.ValueKind != JsonValueKind.Array)
        {
            return new PixelRect(0, 0, imageWidth, imageHeight);
        }

        var points = vertices.EnumerateArray()
            .Select(vertex => (
                X: vertex.TryGetProperty("x", out var x) ? x.GetInt32() : 0,
                Y: vertex.TryGetProperty("y", out var y) ? y.GetInt32() : 0))
            .ToArray();
        if (points.Length == 0)
        {
            return new PixelRect(0, 0, imageWidth, imageHeight);
        }

        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        return new PixelRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static string? ReadDetectedLanguage(JsonElement response)
    {
        if (response.TryGetProperty("textAnnotations", out var annotations) &&
            annotations.ValueKind == JsonValueKind.Array &&
            annotations.GetArrayLength() > 0 &&
            annotations[0].TryGetProperty("locale", out var locale))
        {
            return FromGoogleLanguageCode(locale.GetString());
        }

        return null;
    }

    private static string ToGoogleLanguageCode(string code) => code == "zh-Hant" ? "zh-TW" : code;

    private static string? FromGoogleLanguageCode(string? code) => code?.ToLowerInvariant() switch
    {
        "zh-tw" or "zh-hk" or "zh-hant" => "zh-Hant",
        "zh-cn" or "zh-hans" => "zh",
        { } value when LanguageCatalog.IsKnown(value) => value,
        _ => null
    };
}

public sealed class GoogleCloudTranslationProvider(HttpClient httpClient, ISecretStore secretStore)
    : ITranslationProvider
{
    private static readonly Uri Endpoint = new("https://translation.googleapis.com/language/translate/v2");

    public ProviderMetadata Metadata { get; } = new(
        "google-translate",
        "Google Cloud Translation",
        ProviderExecutionLocation.Cloud,
        UploadsImage: false,
        RequiresSecret: true,
        LanguageCatalog.Codes);

    public async ValueTask<ProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = await secretStore.GetAsync(SecretKeys.GoogleCloudApiKey, cancellationToken);
        return string.IsNullOrWhiteSpace(apiKey)
            ? new ProviderAvailability(false, "Please configure your Google Cloud API key first.")
            : ProviderAvailability.Available;
    }

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!LanguageCatalog.IsKnown(request.TargetLanguage))
        {
            throw new ProviderException(
                "google_target_language_unsupported",
                $"Google Cloud Translation target language is invalid: {request.TargetLanguage}.");
        }

        var apiKey = await secretStore.GetAsync(SecretKeys.GoogleCloudApiKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ProviderException("credentials_missing", "Google Cloud API key is not configured.");
        }

        var payload = new JsonObject
        {
            ["q"] = request.Text,
            ["target"] = ToGoogleLanguageCode(request.TargetLanguage),
            ["format"] = "text"
        };
        if (request.SourceLanguage != LanguageCatalog.Auto && LanguageCatalog.IsKnown(request.SourceLanguage))
        {
            payload["source"] = ToGoogleLanguageCode(request.SourceLanguage);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        message.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(message, cancellationToken);
        var responsePayload = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(responsePayload);
        }
        catch (JsonException exception)
        {
            throw new ProviderException(
                "google_translate_schema",
                $"Google Cloud Translation returned HTTP {(int)response.StatusCode} with an invalid response.",
                exception);
        }

        using (document)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = document.RootElement.TryGetProperty("error", out var errorElement) &&
                            errorElement.TryGetProperty("message", out var errorMessage)
                    ? errorMessage.GetString()
                    : response.ReasonPhrase;
                throw new ProviderException(
                    "google_translate_http",
                    $"Google Cloud Translation returned HTTP {(int)response.StatusCode}: {error ?? "Request failed"}");
            }

            try
            {
                var translation = document.RootElement
                    .GetProperty("data")
                    .GetProperty("translations")[0];
                var text = WebUtility.HtmlDecode(
                    translation.GetProperty("translatedText").GetString() ?? string.Empty).Trim();
                var detectedSource = translation.TryGetProperty("detectedSourceLanguage", out var detected)
                    ? detected.GetString()
                    : null;
                return new TranslationResult(
                    text,
                    string.IsNullOrWhiteSpace(detectedSource)
                        ? request.SourceLanguage
                        : FromGoogleLanguageCode(detectedSource) ?? request.SourceLanguage,
                    request.TargetLanguage);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
            {
                throw new ProviderException(
                    "google_translate_schema",
                    "Google Cloud Translation response is missing translated text.",
                    exception);
            }
        }
    }

    public async Task ValidateCredentialsAsync(CancellationToken cancellationToken = default)
    {
        _ = await TranslateAsync(new TranslationRequest("test", "en", "zh"), cancellationToken);
    }

    private static string ToGoogleLanguageCode(string code) => code switch
    {
        "zh" => "zh-CN",
        "zh-Hant" => "zh-TW",
        _ => code
    };

    private static string? FromGoogleLanguageCode(string? code) => code?.ToLowerInvariant() switch
    {
        "zh-cn" or "zh" => "zh",
        "zh-tw" or "zh-hk" => "zh-Hant",
        "iw" => "he",
        "fil" => "tl",
        { } value when LanguageCatalog.IsKnown(value) => value,
        _ => null
    };
}

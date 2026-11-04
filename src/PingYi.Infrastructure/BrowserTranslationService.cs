using System.Security.Cryptography;
using System.Text;
using PingYi.Core;
using SkiaSharp;

namespace PingYi.Infrastructure;

public sealed class BrowserTranslationService(ProviderRegistry providers,
    Func<CancellationToken, Task> ensureRuntime,
    Func<AppSettings, ITranslationProvider>? createTranslator = null,
    Func<AppSettings, IOcrProvider>? createOcr = null)
{
    private ITranslationProvider Translator(AppSettings settings) => createTranslator?.Invoke(settings) ?? providers.GetTranslationProvider(settings.TranslationProviderId);
    private IOcrProvider Ocr(AppSettings settings) => createOcr?.Invoke(settings) ?? providers.GetOcrProvider(settings.OcrProviderId);
    private static bool IsRemote(ProviderMetadata provider, AppSettings settings) =>
        provider.Location == ProviderExecutionLocation.Cloud ||
        provider.Location == ProviderExecutionLocation.Configurable &&
        (!Uri.TryCreate(settings.CustomTranslationEndpoint, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback);

    public BrowserStatus GetStatus(AppSettings settings)
    {
        var translation = Translator(settings).Metadata;
        var ocr = Ocr(settings).Metadata;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{ConfigurationRouting.Fingerprint(settings, ModelTasks.Ocr)}|{ConfigurationRouting.Fingerprint(settings, ModelTasks.Translation)}")));
        return new(translation.DisplayName, ocr.DisplayName, IsRemote(translation, settings),
            IsRemote(ocr, settings), key, settings.TargetLanguage, LanguageCatalog.All);
    }

    public async Task<BrowserResponse> HandleAsync(BrowserRequest request, AppSettings settings, CancellationToken token)
    {
        var status = GetStatus(settings);
        if (request.Operation == "status") return new(true, Status: status);
        if (request.Operation is not ("translate" or "ocr")) return new(false, "unsupported_operation");
        if (request.RouteKey != status.RouteKey) return new(false, "settings_changed");
        if (status.RemoteText && !request.AllowRemoteText) return new(false, "remote_text_consent_required");
        if (request.Operation == "ocr" && status.RemoteImage && !request.AllowRemoteImage)
            return new(false, "remote_image_consent_required");
        if (request.SourceLanguage != "auto" && !LanguageCatalog.IsKnown(request.SourceLanguage) ||
            request.TargetLanguage != "auto-opposite" && !LanguageCatalog.IsKnown(request.TargetLanguage))
            return new(false, "invalid_language");
        var text = request.Text ?? "";
        var detected = request.DetectedLanguage;
        if (request.Operation == "translate" && (string.IsNullOrWhiteSpace(text) || text.Length > 12000))
            return new(false, "invalid_text");

        ImageFrame? image = null;
        if (request.Operation == "ocr")
        {
            if (request.Image is null || request.Image.Length > 11 * 1024 * 1024) return new(false, "invalid_image");
            try
            {
                var bytes = Convert.FromBase64String(request.Image);
                using var data = SKData.CreateCopy(bytes);
                using var codec = SKCodec.Create(data);
                if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Png ||
                    codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
                    (long)codec.Info.Width * codec.Info.Height > 16_000_000) return new(false, "invalid_image");
                image = new(bytes, codec.Info.Width, codec.Info.Height, new(0, 0, codec.Info.Width, codec.Info.Height));
            }
            catch (FormatException) { return new(false, "invalid_image"); }
        }

        if (RuntimePolicy.UsesManagedRuntime(settings)) await ensureRuntime(token);
        if (image is not null)
        {
            var ocr = Ocr(settings);
            if (!(await ocr.GetAvailabilityAsync(token)).IsAvailable) return new(false, "ocr_unavailable");
            var recognized = await ocr.RecognizeAsync(image, new(request.SourceLanguage), token);
            text = recognized.PlainText;
            detected = recognized.DetectedLanguage;
        }
        if (string.IsNullOrWhiteSpace(text)) return new(false, "no_text");
        if (text.Length > 12000) return new(false, "text_too_long");
        var provider = Translator(settings);
        var source = request.SourceLanguage == "auto"
            ? LanguageCatalog.IsKnown(detected) ? detected! : TextProcessing.DetectLanguage(text)
            : request.SourceLanguage;
        var target = TextProcessing.ResolveTargetLanguage(source, request.TargetLanguage);
        if (source == target) return new(true, Original: text, Translation: text, SourceLanguage: source, TargetLanguage: target);
        if (!(await provider.GetAvailabilityAsync(token)).IsAvailable) return new(false, "translation_unavailable");
        var route = TextProcessing.ResolveTranslationLanguages(request.SourceLanguage, target, source, text,
            provider.Metadata.Id != "local-argos");
        var result = await provider.TranslateAsync(new(text, route.SourceLanguage, route.TargetLanguage), token);
        if (result.Text.Length > 100000) return new(false, "result_too_long");
        return new(true, Original: text, Translation: result.Text, SourceLanguage: source, TargetLanguage: target);
    }
}

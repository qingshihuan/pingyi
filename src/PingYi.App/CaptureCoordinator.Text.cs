using PingYi.Core;

namespace PingYi.App;

public sealed partial class CaptureCoordinator
{
    private readonly OcrRetryCache _ocrRetryCache = new();
    private async Task ProcessTextAsync(OperationContext operation, ResultWindow window, ImageFrame image)
    {
        var settings = services.Settings;
        var ocr = services.TaskOcr(settings);
        var translator = services.TaskTranslator(settings);
        var privacy = UiText.IsEnglish ? $"Current model: {ocr.Metadata.DisplayName}\nRemote requests send the selected image and recognized text to this service."
            : $"当前模型：{ocr.Metadata.DisplayName}\n远程请求会向此服务发送所选图片与识别文字。";
        var label = ocr.Metadata.DisplayName;
        if (HasRemoteProvider(settings, ocr.Metadata, translator.Metadata))
        {
            var approved = await services.AuthorizeRemoteCaptureAsync(settings,
                token => AutomaticConsentWindow.ConfirmAsync(window, privacy, token), operation.Token);
            if (approved is null)
            {
                EnsureCurrent(operation);
                window.SetAnalysisCancelled(CaptureUiText.NotSent);
                return;
            }
            settings = approved;
            EnsureCurrent(operation);
            if (services.Settings != settings) { window.SetError(CaptureUiText.SettingsChanged); return; }
        }
        if (RuntimePolicy.UsesManagedRuntime(settings))
        {
            try
            {
                window.SetLoading(UiText.Get("String.ModelLoadingWait"), privacy);
                await WithTimeoutAsync(token => WaitForModelAsync(operation, window, token), ManagedRuntimeTimeout,
                    operation.Token, "managed_runtime_timeout", UiText.Get("String.ModelStartTimeout"));
                EnsureCurrent(operation);
                if (services.Settings != settings) { window.SetError(CaptureUiText.SettingsChanged); return; }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { EnsureCurrent(operation); window.SetError(VisionErrors.Describe(error)); return; }
        }
        window.SetLoading(UiText.IsEnglish ? $"Recognizing with {label}…" : $"正在使用 {label} 识别…", privacy);
        OcrResult result;
        var reused = _ocrRetryCache.TryGetForTranslationRetry(image, settings, out var retryOcr, out var retryLabel);
        if (reused) { result = retryOcr; label = retryLabel; }
        else
        try
        {
            var availability = await WithTimeoutAsync(token => ocr.GetAvailabilityAsync(token).AsTask(), AvailabilityTimeout,
                operation.Token, "ocr_availability_timeout", "OCR 引擎状态检查超时。");
            EnsureCurrent(operation);
            if (!availability.IsAvailable) throw new ProviderException("ocr_unavailable", availability.Message ?? "OCR 引擎不可用。");
            result = await WithTimeoutAsync(token => ocr.RecognizeAsync(image, new OcrOptions(settings.SourceLanguage), token),
                OcrTimeout, operation.Token, "ocr_timeout", "文字识别超时，请缩小截图范围后重试。");
            EnsureCurrent(operation);
            if (string.IsNullOrWhiteSpace(result.PlainText)) throw new ProviderException("no_text", "所选区域中没有识别到文字。");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { EnsureCurrent(operation); window.SetError(UiText.Error(error)); return; }
        EnsureCurrent(operation);
        window.SetSource(result, label);
        _ocrRetryCache.Remember(image, settings, result, label);
        try
        {
            var route = TextProcessing.ResolveTranslationLanguages(settings.SourceLanguage, settings.TargetLanguage,
                result.DetectedLanguage, result.PlainText, translator.Metadata.SupportedLanguages.Count > 2);
            var request = new TranslationRequest(result.PlainText, route.SourceLanguage, route.TargetLanguage);
            var translated = await WithTimeoutAsync(token => translator.TranslateAsync(request, token), TranslationTimeout,
                operation.Token, "translation_timeout", "翻译等待超时，请重试。");
            EnsureCurrent(operation);
            _ocrRetryCache.MarkTranslationComplete(image);
            window.SetTranslation(translated, translator.Metadata.DisplayName);

        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { EnsureCurrent(operation); window.SetError(UiText.Error(error), keepSource: true); }
    }

    internal static bool HasRemoteProvider(AppSettings settings, params ProviderMetadata[] providers) =>
        providers.Any(provider => provider.Location == ProviderExecutionLocation.Cloud ||
            (provider.Location == ProviderExecutionLocation.Configurable && !new Uri(ConfigurationRouting.Resolve(settings, ModelTasks.Ocr).Settings.CustomTranslationEndpoint).IsLoopback));
}

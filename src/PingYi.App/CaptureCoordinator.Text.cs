using PingYi.Core;

namespace PingYi.App;

public sealed partial class CaptureCoordinator
{
    private async Task ProcessTextAsync(OperationContext operation, ResultWindow window, ImageFrame image)
    {
        var settings = services.Settings;
        var ocr = services.CaptureOcr(settings);
        var translator = services.CaptureTranslator(settings);
        var privacy = BuildPrivacyDescription(settings, ocr.Metadata, translator.Metadata);
        var label = UiText.ProviderName(ocr.Metadata.Id, ocr.Metadata.DisplayName);
        if (window.RequestedPurpose == CapturePurpose.Auto && HasRemoteProvider(settings, ocr.Metadata, translator.Metadata))
        {
            if (!await AutomaticConsentWindow.ConfirmAsync(window, privacy, operation.Token))
            {
                EnsureCurrent(operation);
                window.SetAnalysisCancelled(CaptureUiText.NotSent);
                return;
            }
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
        try
        {
            var availability = await WithTimeoutAsync(token => ocr.GetAvailabilityAsync(token).AsTask(), AvailabilityTimeout,
                operation.Token, "ocr_availability_timeout", "OCR 引擎状态检查超时。");
            EnsureCurrent(operation);
            if (!availability.IsAvailable) throw new ProviderException("ocr_unavailable", availability.Message ?? "OCR 引擎不可用。");
            // Local probe results are reusable only for the same provider and source settings.
            result = ReferenceEquals(ocr, services.PaddleProvider) && TryGetPaddleProbe(image, settings.SourceLanguage, out var cached)
                ? cached
                : await WithTimeoutAsync(token => ocr.RecognizeAsync(image, new OcrOptions(settings.SourceLanguage), token),
                    OcrTimeout, operation.Token, "ocr_timeout", "文字识别超时，请缩小截图范围后重试。");
            EnsureCurrent(operation);
            if (string.IsNullOrWhiteSpace(result.PlainText)) throw new ProviderException("no_text", "所选区域中没有识别到文字。");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception primary) when (!ReferenceEquals(ocr, services.PaddleProvider))
        {
            try
            {
                var availability = await WithTimeoutAsync(token => services.PaddleProvider.GetAvailabilityAsync(token).AsTask(),
                    AvailabilityTimeout, operation.Token, "ocr_fallback_availability_timeout", "本地 PaddleOCR 状态检查超时。");
                EnsureCurrent(operation);
                if (!availability.IsAvailable) throw new ProviderException("ocr_fallback_unavailable", availability.Message ?? "本地 PaddleOCR 回退不可用。");
                result = TryGetPaddleProbe(image, settings.SourceLanguage, out var cached) ? cached
                    : await WithTimeoutAsync(token => services.PaddleProvider.RecognizeAsync(image, new OcrOptions(settings.SourceLanguage), token),
                        OcrTimeout, operation.Token, "ocr_fallback_timeout", "本地 PaddleOCR 回退识别超时。");
                EnsureCurrent(operation);
                if (string.IsNullOrWhiteSpace(result.PlainText)) throw new ProviderException("no_text", "PaddleOCR 回退也没有识别到文字。");
                label = UiText.IsEnglish ? "PaddleOCR (selected OCR unavailable; lightweight fallback)" : "PaddleOCR（所选 OCR 不可用，轻量回退）";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception fallback)
            {
                EnsureCurrent(operation);
                window.SetError(UiText.Error(new ProviderException("ocr_primary_and_fallback_failed",
                    $"{primary.Message}；本地 OCR 回退也不可用：{fallback.Message}", primary)));
                return;
            }
        }
        catch (Exception error) { EnsureCurrent(operation); window.SetError(UiText.Error(error)); return; }
        EnsureCurrent(operation);
        window.SetSource(result, label);
        try
        {
            var route = TextProcessing.ResolveTranslationLanguages(settings.SourceLanguage, settings.TargetLanguage,
                result.DetectedLanguage, result.PlainText, translator.Metadata.SupportedLanguages.Count > 2);
            var request = new TranslationRequest(result.PlainText, route.SourceLanguage, route.TargetLanguage);
            var execution = await WithTimeoutAsync(token => TranslationFallback.ExecuteAsync(translator, services.ArgosProvider, request, token),
                TranslationTimeout, operation.Token, "translation_timeout", "翻译超时，可复制原文或重试。");
            EnsureCurrent(operation);
            var name = UiText.ProviderName(execution.Provider.Id, execution.Provider.DisplayName);
            window.SetTranslation(execution.Result, execution.UsedFallback
                ? UiText.IsEnglish ? $"{name} (lightweight fallback)" : $"{name}（轻量回退）" : name);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { EnsureCurrent(operation); window.SetError(UiText.Error(error), keepSource: true); }
    }

    internal static bool HasRemoteProvider(AppSettings settings, params ProviderMetadata[] providers) =>
        providers.Any(provider => provider.Location == ProviderExecutionLocation.Cloud ||
            (provider.Location == ProviderExecutionLocation.Configurable && !ProcessingModes.IsLocalEndpoint(settings)));
}

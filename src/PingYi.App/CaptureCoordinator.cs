using PingYi.Core;

namespace PingYi.App;

public sealed class CaptureCoordinator(AppServices services)
{
    private readonly SemaphoreSlim _captureGate = new(1, 1);

    public async Task StartCaptureAsync(IMainWindowShell? mainWindow)
    {
        if (!await _captureGate.WaitAsync(0))
        {
            mainWindow?.SetGlobalStatus("已有截图任务正在进行。", isError: true);
            return;
        }

        var restoreMainWindow = mainWindow?.IsVisible == true;
        try
        {
            mainWindow?.Hide();
            await Task.Delay(140);
            var desktop = await services.ScreenCaptureService.CaptureDesktopAsync();
            var overlay = new CaptureOverlayWindow(desktop);
            var selection = await overlay.ShowAndSelectAsync();
            if (selection is null)
            {
                if (restoreMainWindow)
                {
                    mainWindow?.Show();
                }
                return;
            }

            var selectedImage = services.ImageCropper.Crop(desktop, selection.Value);
            var resultWindow = new ResultWindow();
            resultWindow.RetryRequested += () => ProcessAsync(resultWindow, selectedImage);
            resultWindow.OpenSettingsRequested += () =>
            {
                mainWindow?.Show();
                mainWindow?.Activate();
            };
            resultWindow.ShowAt(selectedImage.DesktopBounds);
            await ProcessAsync(resultWindow, selectedImage);
        }
        catch (Exception exception)
        {
            if (restoreMainWindow)
            {
                mainWindow?.Show();
            }
            mainWindow?.SetGlobalStatus(exception.Message, isError: true);
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private async Task ProcessAsync(ResultWindow window, ImageFrame image)
    {
        var settings = services.Settings;
        var ocrProvider = services.Providers.GetOcrProvider(settings.OcrProviderId);
        var translationProvider = services.Providers.GetTranslationProvider(settings.TranslationProviderId);
        var privacy = BuildPrivacyDescription(ocrProvider.Metadata, translationProvider.Metadata);
        window.SetLoading($"正在使用 {ocrProvider.Metadata.DisplayName} 识别…", privacy);

        OcrResult ocrResult;
        try
        {
            var availability = await ocrProvider.GetAvailabilityAsync();
            if (!availability.IsAvailable)
            {
                throw new ProviderException("ocr_unavailable", availability.Message ?? "OCR 引擎不可用。");
            }

            ocrResult = await ocrProvider.RecognizeAsync(image, new OcrOptions(settings.SourceLanguage));
            if (string.IsNullOrWhiteSpace(ocrResult.PlainText))
            {
                throw new ProviderException("no_text", "所选区域中没有识别到文字。");
            }
            window.SetSource(ocrResult, ocrProvider.Metadata.DisplayName);
        }
        catch (Exception exception)
        {
            window.SetError(exception.Message);
            return;
        }

        try
        {
            var configuredTarget = settings.TargetLanguage;
            var useModelLanguageDetection =
                configuredTarget != LanguageCatalog.AutoOpposite &&
                translationProvider.Metadata.SupportedLanguages.Count > 2;
            var sourceLanguage = settings.SourceLanguage != LanguageCatalog.Auto
                ? LanguageCatalog.NormalizeSource(settings.SourceLanguage)
                : useModelLanguageDetection
                    ? LanguageCatalog.Auto
                    : ocrResult.DetectedLanguage is "zh" or "en"
                        ? ocrResult.DetectedLanguage
                        : TextProcessing.DetectLanguage(ocrResult.PlainText);
            var targetLanguage = TextProcessing.ResolveTargetLanguage(sourceLanguage, settings.TargetLanguage);
            var request = new TranslationRequest(ocrResult.PlainText, sourceLanguage, targetLanguage);
            var execution = await TranslationFallback.ExecuteAsync(
                translationProvider,
                services.ArgosProvider,
                request);
            var providerLabel = execution.UsedFallback
                ? $"{execution.Provider.DisplayName}（外部服务不可用，已自动回退）"
                : execution.Provider.DisplayName;
            window.SetTranslation(execution.Result, providerLabel);
        }
        catch (Exception exception)
        {
            window.SetError(exception.Message, keepSource: true);
        }
    }

    private static string BuildPrivacyDescription(ProviderMetadata ocr, ProviderMetadata translation)
    {
        if (ocr.Location == ProviderExecutionLocation.Local && translation.Location == ProviderExecutionLocation.Local)
        {
            return "全程本地处理 · 内容不离开设备";
        }

        if (ocr.UploadsImage)
        {
            return $"所选图片将发送给 {ocr.DisplayName}；文字将发送给 {translation.DisplayName}";
        }

        return $"图片在本地识别；文字将发送给 {translation.DisplayName}";
    }
}

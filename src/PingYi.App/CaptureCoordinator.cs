using PingYi.Core;

namespace PingYi.App;

public sealed class CaptureCoordinator(AppServices services) : IDisposable
{
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private ResultWindow? _resultWindow;
    private ImageFrame? _lastSelectedImage;
    private IMainWindowShell? _mainWindow;
    private bool _disposed;

    public async Task StartCaptureAsync(IMainWindowShell? mainWindow)
    {
        if (_disposed)
        {
            return;
        }
        if (!await _captureGate.WaitAsync(0))
        {
            mainWindow?.SetGlobalStatus("已有截图任务正在进行。", isError: true);
            return;
        }

        _mainWindow = mainWindow;
        var restoreMainWindow = mainWindow?.IsVisible == true;
        var restoreResultWindow = _resultWindow?.IsVisible == true;
        try
        {
            mainWindow?.Hide();
            _resultWindow?.Hide();
            await Task.Delay(140);
            var desktop = await services.ScreenCaptureService.CaptureDesktopAsync();
            var displays = mainWindow?.GetCaptureDisplays();
            if (displays is null || displays.Count == 0)
            {
                displays = [new CaptureDisplay(desktop.DesktopBounds, 1)];
            }
            var overlay = new CaptureOverlaySession(desktop, displays, services.ImageCropper);
            var selection = await overlay.ShowAndSelectAsync();
            if (selection is null)
            {
                if (restoreMainWindow)
                {
                    mainWindow?.Show();
                }
                if (restoreResultWindow)
                {
                    _resultWindow?.ShowCurrent();
                }
                return;
            }

            var selectedImage = services.ImageCropper.Crop(desktop, selection.Value);
            _lastSelectedImage = selectedImage;
            var resultWindow = GetResultWindow();
            resultWindow.ShowAt(selectedImage.DesktopBounds);
            await ProcessAsync(resultWindow, selectedImage);
        }
        catch (Exception exception)
        {
            if (restoreMainWindow)
            {
                mainWindow?.Show();
            }
            if (restoreResultWindow)
            {
                _resultWindow?.ShowCurrent();
            }
            mainWindow?.SetGlobalStatus(exception.Message, isError: true);
        }
        finally
        {
            _captureGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _resultWindow?.ClosePermanently();
        _resultWindow = null;
        _lastSelectedImage = null;
    }

    private ResultWindow GetResultWindow()
    {
        if (_resultWindow is not null)
        {
            return _resultWindow;
        }

        _resultWindow = new ResultWindow();
        _resultWindow.RetryRequested += RetryLastSelectionAsync;
        _resultWindow.OpenSettingsRequested += () =>
        {
            _mainWindow?.Show();
            _mainWindow?.Activate();
        };
        return _resultWindow;
    }

    private Task RetryLastSelectionAsync() =>
        _resultWindow is not null && _lastSelectedImage is not null
            ? ProcessAsync(_resultWindow, _lastSelectedImage)
            : Task.CompletedTask;

    private async Task ProcessAsync(ResultWindow window, ImageFrame image)
    {
        var settings = services.Settings;
        var ocrProvider = services.Providers.GetOcrProvider(settings.OcrProviderId);
        var translationProvider = services.Providers.GetTranslationProvider(settings.TranslationProviderId);
        var privacy = BuildPrivacyDescription(settings, ocrProvider.Metadata, translationProvider.Metadata);
        var selectedOcrName = UiText.ProviderName(ocrProvider.Metadata.Id, ocrProvider.Metadata.DisplayName);
        window.SetLoading(
            UiText.IsEnglish ? $"Recognizing with {selectedOcrName}…" : $"正在使用 {selectedOcrName} 识别…",
            privacy);

        OcrResult ocrResult;
        var ocrProviderLabel = selectedOcrName;
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
        }
        catch (Exception primaryFailure) when (!ReferenceEquals(ocrProvider, services.PaddleProvider))
        {
            try
            {
                var fallbackAvailability = await services.PaddleProvider.GetAvailabilityAsync();
                if (!fallbackAvailability.IsAvailable)
                {
                    throw new ProviderException(
                        "ocr_fallback_unavailable",
                        fallbackAvailability.Message ?? "本地 PaddleOCR 回退不可用。");
                }

                ocrResult = await services.PaddleProvider.RecognizeAsync(
                    image,
                    new OcrOptions(settings.SourceLanguage));
                if (string.IsNullOrWhiteSpace(ocrResult.PlainText))
                {
                    throw new ProviderException("no_text", "PaddleOCR 回退也没有识别到文字。");
                }

                ocrProviderLabel = UiText.IsEnglish
                    ? $"{UiText.ProviderName(services.PaddleProvider.Metadata.Id, services.PaddleProvider.Metadata.DisplayName)} (selected OCR unavailable; fallback used)"
                    : $"{services.PaddleProvider.Metadata.DisplayName}（所选 OCR 不可用，已回退）";
            }
            catch (Exception fallbackFailure)
            {
                window.SetError($"{primaryFailure.Message}；本地 OCR 回退也不可用：{fallbackFailure.Message}");
                return;
            }
        }
        catch (Exception exception)
        {
            window.SetError(exception.Message);
            return;
        }

        window.SetSource(ocrResult, ocrProviderLabel);

        try
        {
            var route = TextProcessing.ResolveTranslationLanguages(
                settings.SourceLanguage,
                settings.TargetLanguage,
                ocrResult.DetectedLanguage,
                ocrResult.PlainText,
                providerCanDetectSourceLanguage: translationProvider.Metadata.SupportedLanguages.Count > 2);
            var request = new TranslationRequest(
                ocrResult.PlainText,
                route.SourceLanguage,
                route.TargetLanguage);
            var execution = await TranslationFallback.ExecuteAsync(
                translationProvider,
                services.ArgosProvider,
                request);
            var executionProviderName = UiText.ProviderName(execution.Provider.Id, execution.Provider.DisplayName);
            var providerLabel = execution.UsedFallback
                ? UiText.IsEnglish
                    ? $"{executionProviderName} (cloud service unavailable; offline fallback used)"
                    : $"{executionProviderName}（外部服务不可用，已自动回退）"
                : executionProviderName;
            window.SetTranslation(execution.Result, providerLabel);
        }
        catch (Exception exception)
        {
            window.SetError(exception.Message, keepSource: true);
        }
    }

    private static string BuildPrivacyDescription(
        AppSettings settings,
        ProviderMetadata ocr,
        ProviderMetadata translation)
    {
        var localModelEndpoint = Uri.TryCreate(
            settings.CustomTranslationEndpoint,
            UriKind.Absolute,
            out var endpoint) && endpoint.IsLoopback;
        var usesLocalVision = (ocr.Id is "local-vlm-ocr" or "local-vlm-corrected") && localModelEndpoint;
        var usesSameLocalTranslation = translation.Id == "custom-chat" && localModelEndpoint;
        if (usesLocalVision && (translation.Location == ProviderExecutionLocation.Local || usesSameLocalTranslation))
        {
            return UiText.T("图片与文字发送到本机大模型服务 · 内容不离开设备");
        }

        if (ocr.Location == ProviderExecutionLocation.Local && translation.Location == ProviderExecutionLocation.Local)
        {
            return UiText.T("全程本地处理 · 内容不离开设备");
        }

        if (ocr.UploadsImage)
        {
            return UiText.IsEnglish
                ? $"The selected image is sent to {UiText.ProviderName(ocr.Id, ocr.DisplayName)}; text is sent to {UiText.ProviderName(translation.Id, translation.DisplayName)}"
                : $"所选图片将发送给 {ocr.DisplayName}；文字将发送给 {translation.DisplayName}";
        }

        return UiText.IsEnglish
            ? $"The image is recognized locally; text is sent to {UiText.ProviderName(translation.Id, translation.DisplayName)}"
            : $"图片在本地识别；文字将发送给 {translation.DisplayName}";
    }
}

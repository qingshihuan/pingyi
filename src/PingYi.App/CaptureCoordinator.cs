using Avalonia.Threading;
using PingYi.Core;

namespace PingYi.App;

public sealed class CaptureCoordinator(AppServices services) : IAsyncDisposable
{
    private const int MaximumPinnedWindows = 5;
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AvailabilityTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ManagedRuntimeTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OcrTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TranslationTimeout = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _operationSync = new();
    private readonly HashSet<OperationContext> _operations = [];
    private readonly TaskCompletionSource _disposeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<ResultWindow> _pinnedWindows = [];
    private readonly Dictionary<ResultWindow, ImageFrame> _windowImages = [];
    private ResultWindow? _resultWindow;
    private IMainWindowShell? _mainWindow;
    private OperationContext? _currentOperation;
    private long _nextOperationId;
    private int _disposeState;

    public async Task StartCaptureAsync(IMainWindowShell? mainWindow)
    {
        var operation = BeginOperation();
        if (operation is null)
        {
            return;
        }

        _mainWindow = mainWindow;
        var enteredGate = false;
        var restoreMainWindow = false;
        var restoreResultWindow = false;
        ResultWindow? hiddenResultWindow = null;
        try
        {
            await _operationGate.WaitAsync(operation.Token);
            enteredGate = true;
            EnsureCurrent(operation);

            if (_resultWindow?.IsPinned == true)
            {
                ArchivePinnedWindow(_resultWindow);
                _resultWindow = null;
            }

            restoreMainWindow = mainWindow?.IsVisible == true;
            hiddenResultWindow = _resultWindow;
            restoreResultWindow = hiddenResultWindow?.IsVisible == true;
            mainWindow?.Hide();
            hiddenResultWindow?.HideTemporarily();

            await Task.Delay(140, operation.Token);
            var desktop = await WithTimeoutAsync(
                token => services.ScreenCaptureService.CaptureDesktopAsync(token),
                CaptureTimeout,
                operation.Token,
                "capture_timeout",
                "屏幕捕获超时，请重试。");
            EnsureCurrent(operation);

            var displays = mainWindow?.GetCaptureDisplays();
            if (displays is null || displays.Count == 0)
            {
                displays = [new CaptureDisplay(desktop.DesktopBounds, 1)];
            }

            var overlay = new CaptureOverlaySession(desktop, displays, services.ImageCropper);
            var selection = await overlay.ShowAndSelectAsync(operation.Token);
            EnsureCurrent(operation);
            if (selection is null)
            {
                RestoreWindows(mainWindow, restoreMainWindow, hiddenResultWindow, restoreResultWindow);
                return;
            }

            var selectedImage = services.ImageCropper.Crop(desktop, selection.Value);
            var resultWindow = GetResultWindow();
            operation.TargetWindow = resultWindow;
            _windowImages[resultWindow] = selectedImage;
            resultWindow.ShowAt(selectedImage.DesktopBounds);
            await ProcessAsync(operation, resultWindow, selectedImage);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        {
            // A newer capture, closing the active result, or application shutdown owns cleanup.
        }
        catch (Exception exception)
        {
            if (IsCurrent(operation))
            {
                RestoreWindows(mainWindow, restoreMainWindow, hiddenResultWindow, restoreResultWindow);
                mainWindow?.SetGlobalStatus(UiText.Error(exception), isError: true);
            }
        }
        finally
        {
            if (enteredGate)
            {
                _operationGate.Release();
            }

            EndOperation(operation);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            await _disposeCompletion.Task;
            return;
        }

        OperationContext[] operations;
        lock (_operationSync)
        {
            _currentOperation = null;
            operations = _operations.ToArray();
        }

        try
        {
            foreach (var operation in operations)
            {
                Cancel(operation);
            }

            // Cancellation only requests that native OCR/translation work stop. Wait for
            // every operation to leave its finally block before AppServices disposes the
            // ONNX, Skia, Argos, or llama.cpp resources used by that work.
            await Task.WhenAll(operations.Select(operation => operation.Completion.Task));

            ResultWindow[] windows;
            lock (_operationSync)
            {
                windows = _pinnedWindows
                    .Append(_resultWindow)
                    .Where(window => window is not null)
                    .Cast<ResultWindow>()
                    .Distinct()
                    .ToArray();
                _pinnedWindows.Clear();
                _resultWindow = null;
                _windowImages.Clear();
            }

            foreach (var window in windows)
            {
                window.ClosePermanently();
            }
        }
        finally
        {
            Volatile.Write(ref _disposeState, 2);
            _disposeCompletion.TrySetResult();
        }
    }

    private OperationContext? BeginOperation(ResultWindow? targetWindow = null)
    {
        OperationContext? previous;
        OperationContext operation;
        lock (_operationSync)
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                return null;
            }

            previous = _currentOperation;
            operation = new OperationContext(
                Interlocked.Increment(ref _nextOperationId),
                new CancellationTokenSource())
            {
                TargetWindow = targetWindow
            };
            _currentOperation = operation;
            _operations.Add(operation);
        }

        // Cancel outside the lock because token callbacks may marshal back to the UI thread.
        Cancel(previous);
        return operation;
    }

    private void EndOperation(OperationContext operation)
    {
        lock (_operationSync)
        {
            if (ReferenceEquals(_currentOperation, operation))
            {
                _currentOperation = null;
            }

            _operations.Remove(operation);
        }

        operation.Cancellation.Dispose();
        operation.Completion.TrySetResult();
    }

    private bool IsCurrent(OperationContext operation)
    {
        lock (_operationSync)
        {
            return Volatile.Read(ref _disposeState) == 0 &&
                   ReferenceEquals(_currentOperation, operation) &&
                   _currentOperation.Id == operation.Id &&
                   !operation.Token.IsCancellationRequested;
        }
    }

    private void EnsureCurrent(OperationContext operation)
    {
        if (!IsCurrent(operation))
        {
            throw new OperationCanceledException(operation.Token);
        }
    }

    private static void Cancel(OperationContext? operation)
    {
        if (operation is null || operation.Cancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            operation.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation completed between lookup and cancellation.
        }
    }

    private ResultWindow GetResultWindow()
    {
        if (_resultWindow is not null)
        {
            return _resultWindow;
        }

        var window = new ResultWindow();
        window.RetryRequested += () => RetrySelectionAsync(window);
        window.OpenSettingsRequested += () =>
        {
            _mainWindow?.OpenSettings();
        };
        window.Dismissed += ResultWindow_OnDismissed;
        _resultWindow = window;
        return window;
    }

    private void ArchivePinnedWindow(ResultWindow window)
    {
        if (!_pinnedWindows.Contains(window))
        {
            _pinnedWindows.Add(window);
        }

        while (_pinnedWindows.Count > MaximumPinnedWindows)
        {
            var oldest = _pinnedWindows[0];
            _pinnedWindows.RemoveAt(0);
            _windowImages.Remove(oldest);
            oldest.ClosePermanently();
        }
    }

    private void ResultWindow_OnDismissed(ResultWindow window)
    {
        OperationContext? operation;
        var discardWindow = false;
        lock (_operationSync)
        {
            operation = _currentOperation;
            if (window.IsPinned)
            {
                _pinnedWindows.Remove(window);
                if (ReferenceEquals(_resultWindow, window))
                {
                    _resultWindow = null;
                }

                _windowImages.Remove(window);
                discardWindow = true;
            }
        }

        if (ReferenceEquals(operation?.TargetWindow, window))
        {
            Cancel(operation);
        }

        if (discardWindow)
        {
            Dispatcher.UIThread.Post(window.ClosePermanently, DispatcherPriority.Background);
        }
    }

    private async Task RetrySelectionAsync(ResultWindow window)
    {
        if (!_windowImages.TryGetValue(window, out var image))
        {
            return;
        }

        var operation = BeginOperation(window);
        if (operation is null)
        {
            return;
        }

        var enteredGate = false;
        try
        {
            await _operationGate.WaitAsync(operation.Token);
            enteredGate = true;
            EnsureCurrent(operation);
            window.ShowCurrent();
            await ProcessAsync(operation, window, image);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        {
            // A newer action superseded this retry.
        }
        catch (Exception exception)
        {
            if (IsCurrent(operation))
            {
                window.SetError(UiText.Error(exception));
            }
        }
        finally
        {
            if (enteredGate)
            {
                _operationGate.Release();
            }

            EndOperation(operation);
        }
    }

    private async Task ProcessAsync(
        OperationContext operation,
        ResultWindow window,
        ImageFrame image)
    {
        var settings = services.Settings;
        var ocrProvider = services.Providers.GetOcrProvider(settings.OcrProviderId);
        var translationProvider = services.Providers.GetTranslationProvider(settings.TranslationProviderId);
        var privacy = BuildPrivacyDescription(settings, ocrProvider.Metadata, translationProvider.Metadata);
        var selectedOcrName = UiText.ProviderName(ocrProvider.Metadata.Id, ocrProvider.Metadata.DisplayName);

        if (settings.ManagedRuntimeEnabled &&
            (ocrProvider.Metadata.Id is "local-vlm-ocr" or "local-vlm-corrected" ||
             translationProvider.Metadata.Id == "custom-chat"))
        {
            try
            {
                EnsureCurrent(operation);
                window.SetLoading(
                    UiText.IsEnglish ? "Starting the local model…" : "正在启动本机大模型…",
                    privacy);
                await WithTimeoutAsync(
                    token => services.WaitForManagedRuntimeAsync(token),
                    ManagedRuntimeTimeout,
                    operation.Token,
                    "managed_runtime_timeout",
                    "本机大模型启动超时，请在设置中检查运行状态。");
                EnsureCurrent(operation);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                EnsureCurrent(operation);
                window.SetError(UiText.Error(exception));
                return;
            }
        }

        EnsureCurrent(operation);
        window.SetLoading(
            UiText.IsEnglish ? $"Recognizing with {selectedOcrName}…" : $"正在使用 {selectedOcrName} 识别…",
            privacy);

        OcrResult ocrResult;
        var ocrProviderLabel = selectedOcrName;
        try
        {
            var availability = await WithTimeoutAsync(
                token => ocrProvider.GetAvailabilityAsync(token).AsTask(),
                AvailabilityTimeout,
                operation.Token,
                "ocr_availability_timeout",
                "OCR 引擎状态检查超时。");
            EnsureCurrent(operation);
            if (!availability.IsAvailable)
            {
                throw new ProviderException("ocr_unavailable", availability.Message ?? "OCR 引擎不可用。");
            }

            ocrResult = await WithTimeoutAsync(
                token => ocrProvider.RecognizeAsync(
                    image,
                    new OcrOptions(settings.SourceLanguage),
                    token),
                OcrTimeout,
                operation.Token,
                "ocr_timeout",
                "文字识别超时，请缩小截图范围后重试。");
            EnsureCurrent(operation);
            if (string.IsNullOrWhiteSpace(ocrResult.PlainText))
            {
                throw new ProviderException("no_text", "所选区域中没有识别到文字。");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception primaryFailure) when (!ReferenceEquals(ocrProvider, services.PaddleProvider))
        {
            try
            {
                var fallbackAvailability = await WithTimeoutAsync(
                    token => services.PaddleProvider.GetAvailabilityAsync(token).AsTask(),
                    AvailabilityTimeout,
                    operation.Token,
                    "ocr_fallback_availability_timeout",
                    "本地 PaddleOCR 状态检查超时。");
                EnsureCurrent(operation);
                if (!fallbackAvailability.IsAvailable)
                {
                    throw new ProviderException(
                        "ocr_fallback_unavailable",
                        fallbackAvailability.Message ?? "本地 PaddleOCR 回退不可用。");
                }

                ocrResult = await WithTimeoutAsync(
                    token => services.PaddleProvider.RecognizeAsync(
                        image,
                        new OcrOptions(settings.SourceLanguage),
                        token),
                    OcrTimeout,
                    operation.Token,
                    "ocr_fallback_timeout",
                    "本地 PaddleOCR 回退识别超时。");
                EnsureCurrent(operation);
                if (string.IsNullOrWhiteSpace(ocrResult.PlainText))
                {
                    throw new ProviderException("no_text", "PaddleOCR 回退也没有识别到文字。");
                }

                var fallbackName = UiText.ProviderName(
                    services.PaddleProvider.Metadata.Id,
                    services.PaddleProvider.Metadata.DisplayName);
                ocrProviderLabel = UiText.IsEnglish
                    ? $"{fallbackName} (selected OCR unavailable; fallback used)"
                    : $"{fallbackName}（所选 OCR 不可用，已回退）";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception fallbackFailure)
            {
                EnsureCurrent(operation);
                var combinedFailure = new ProviderException(
                    "ocr_primary_and_fallback_failed",
                    $"{primaryFailure.Message}；本地 OCR 回退也不可用：{fallbackFailure.Message}",
                    primaryFailure);
                window.SetError(UiText.Error(combinedFailure));
                return;
            }
        }
        catch (Exception exception)
        {
            EnsureCurrent(operation);
            window.SetError(UiText.Error(exception));
            return;
        }

        EnsureCurrent(operation);
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
            var execution = await WithTimeoutAsync(
                token => TranslationFallback.ExecuteAsync(
                    translationProvider,
                    services.ArgosProvider,
                    request,
                    token),
                TranslationTimeout,
                operation.Token,
                "translation_timeout",
                "翻译超时，可复制原文或重试。");
            EnsureCurrent(operation);
            var executionProviderName = UiText.ProviderName(
                execution.Provider.Id,
                execution.Provider.DisplayName);
            var providerLabel = execution.UsedFallback
                ? UiText.IsEnglish
                    ? $"{executionProviderName} (cloud service unavailable; offline fallback used)"
                    : $"{executionProviderName}（外部服务不可用，已自动回退）"
                : executionProviderName;
            window.SetTranslation(execution.Result, providerLabel);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            EnsureCurrent(operation);
            window.SetError(UiText.Error(exception), keepSource: true);
        }
    }

    private static async Task<T> WithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> action,
        TimeSpan timeout,
        CancellationToken operationToken,
        string errorCode,
        string timeoutMessage)
    {
        using var phaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
        phaseCancellation.CancelAfter(timeout);
        try
        {
            return await action(phaseCancellation.Token);
        }
        catch (OperationCanceledException exception) when (!operationToken.IsCancellationRequested)
        {
            throw new ProviderException(errorCode, timeoutMessage, exception);
        }
    }

    private static void RestoreWindows(
        IMainWindowShell? mainWindow,
        bool restoreMainWindow,
        ResultWindow? resultWindow,
        bool restoreResultWindow)
    {
        if (restoreMainWindow)
        {
            mainWindow?.Show();
        }

        if (restoreResultWindow)
        {
            resultWindow?.ShowCurrent();
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

    private sealed class OperationContext(long id, CancellationTokenSource cancellation)
    {
        public long Id { get; } = id;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public CancellationToken Token => Cancellation.Token;
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ResultWindow? TargetWindow { get; set; }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public sealed partial class CaptureCoordinator(AppServices services) : IAsyncDisposable
{
    private const int MaximumPinnedWindows = 5;
    private static readonly TimeSpan AvailabilityTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ManagedRuntimeTimeout = ManagedRuntimeReadiness.OperationTimeout;
    private static readonly TimeSpan OcrTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TranslationTimeout = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _operationSync = new();
    private readonly HashSet<OperationContext> _operations = [];
    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<ResultWindow> _pinnedWindows = [];
    private readonly Dictionary<ResultWindow, ImageFrame> _windowImages = [];
    private ResultWindow? _resultWindow;
    private IMainWindowShell? _mainWindow;
    private OperationContext? _currentOperation;
    private long _nextOperationId;
    private int _disposeState;
    public bool IsCapturingScreen { get; private set; }

    public async Task StartCaptureAsync(IMainWindowShell? mainWindow, CapturePurpose purpose = CapturePurpose.Auto)
    {
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        if (purpose == CapturePurpose.Auto && !services.Settings.AutomaticCaptureEnabled) purpose = CapturePurpose.TranslateText;
        var operation = BeginOperation();
        if (operation is null) return;
        _mainWindow = mainWindow;
        var enteredGate = false;
        try
        {
            await _operationGate.WaitAsync(operation.Token);
            enteredGate = true;
            EnsureCurrent(operation);
            if (_resultWindow?.IsPinned == true) { ArchivePinnedWindow(_resultWindow); _resultWindow = null; }
            var displays = mainWindow?.GetCaptureDisplays();
            var windows = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.Windows.ToList() : new List<Window>();
            if (mainWindow is Window shell) windows.Add(shell);
            windows.AddRange(_pinnedWindows);
            if (_resultWindow is not null) windows.Add(_resultWindow);
            ImageFrame? image;
            IsCapturingScreen = true;
            try
            {
                image = await CaptureWindowScope.RunAsync(windows, DesktopCaptureBarrier.WaitAsync, async token =>
                    await CaptureSelectionRouter.SelectAsync(services.ScreenCaptureService,
                        async (frame, selectionToken) =>
                        {
                            EnsureCurrent(operation);
                            var captureDisplays = displays is { Count: > 0 } ? displays : new[] { new CaptureDisplay(frame.DesktopBounds, 1) };
                            var overlay = new CaptureOverlaySession(frame, captureDisplays, services.ImageCropper);
                            var selection = await overlay.ShowAndSelectAsync(selectionToken);
                            EnsureCurrent(operation);
                            return selection is null ? null : services.ImageCropper.Crop(frame, selection.Value);
                        }, token), operation.Token, () => Volatile.Read(ref _disposeState) == 0);
            }
            finally { IsCapturingScreen = false; }
            EnsureCurrent(operation);
            if (image is null) return;
            var window = GetResultWindow();
            operation.TargetWindow = window;
            window.BeginPurpose(purpose);
            _windowImages[window] = image;
            window.ShowAt(image.DesktopBounds);
            await ProcessAsync(operation, window, image);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (IsCurrent(operation))
            {
                mainWindow?.Show(); mainWindow?.Activate();
                mainWindow?.SetGlobalStatus(LinuxDesktopUi.DescribeError(error), true);
            }
        }
        finally { if (enteredGate) _operationGate.Release(); EndOperation(operation); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0) { await _disposeCompletion.Task; return; }
        OperationContext[] operations;
        lock (_operationSync) { _currentOperation = null; operations = _operations.ToArray(); }
        try
        {
            foreach (var operation in operations) Cancel(operation);
            // Wait for native work to leave its finally blocks before AppServices disposes it.
            await Task.WhenAll(operations.Select(operation => operation.Completion.Task));
            ResultWindow[] windows;
            lock (_operationSync)
            {
                windows = _pinnedWindows.Append(_resultWindow).Where(window => window is not null).Cast<ResultWindow>().Distinct().ToArray();
                _pinnedWindows.Clear(); _resultWindow = null; _windowImages.Clear();
            }
            foreach (var window in windows) window.ClosePermanently();
        }
        finally { Volatile.Write(ref _disposeState, 2); _disposeCompletion.TrySetResult(); }
    }
    private OperationContext? BeginOperation(ResultWindow? targetWindow = null)
    {
        OperationContext? previous;
        OperationContext operation;
        lock (_operationSync)
        {
            if (Volatile.Read(ref _disposeState) != 0) return null;
            previous = _currentOperation;
            operation = new OperationContext(Interlocked.Increment(ref _nextOperationId), new CancellationTokenSource()) { TargetWindow = targetWindow };
            _currentOperation = operation; _operations.Add(operation);
        }
        Cancel(previous); // Outside the lock: callbacks may marshal to the UI thread.
        return operation;
    }
    private void EndOperation(OperationContext operation)
    {
        lock (_operationSync)
        {
            if (ReferenceEquals(_currentOperation, operation)) _currentOperation = null;
            _operations.Remove(operation);
        }
        operation.Cancellation.Dispose(); operation.Completion.TrySetResult();
    }
    private bool IsCurrent(OperationContext operation)
    {
        lock (_operationSync) return Volatile.Read(ref _disposeState) == 0 &&
            ReferenceEquals(_currentOperation, operation) && _currentOperation.Id == operation.Id && !operation.Token.IsCancellationRequested;
    }
    private void EnsureCurrent(OperationContext operation)
    { if (!IsCurrent(operation)) throw new OperationCanceledException(operation.Token); }
    private static void Cancel(OperationContext? operation)
    {
        if (operation is null || operation.Cancellation.IsCancellationRequested) return;
        try { operation.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
    }
    private ResultWindow GetResultWindow()
    {
        if (_resultWindow is not null) return _resultWindow;
        var window = new ResultWindow();
        window.RetryRequested += () => RetrySelectionAsync(window);
        window.AnalyzeRequested += purpose => RetryAsImageAnalysisAsync(window, purpose);
        window.CancelRequested += () => CancelForWindow(window);
        window.OpenSettingsRequested += () => _mainWindow?.OpenSettings();
        window.Dismissed += ResultWindow_OnDismissed;
        return _resultWindow = window;
    }
    private void ArchivePinnedWindow(ResultWindow window)
    {
        if (!_pinnedWindows.Contains(window)) _pinnedWindows.Add(window);
        while (_pinnedWindows.Count > MaximumPinnedWindows)
        {
            var oldest = _pinnedWindows[0]; _pinnedWindows.RemoveAt(0); _windowImages.Remove(oldest); oldest.ClosePermanently();
        }
    }
    private void ResultWindow_OnDismissed(ResultWindow window)
    {
        OperationContext? operation;
        var discard = false;
        lock (_operationSync)
        {
            operation = _currentOperation;
            _windowImages.Remove(window); // Closing ends this screenshot's in-memory session.
            if (window.IsPinned)
            {
                _pinnedWindows.Remove(window);
                if (ReferenceEquals(_resultWindow, window)) _resultWindow = null;
                discard = true;
            }
        }
        if (ReferenceEquals(operation?.TargetWindow, window)) Cancel(operation);
        if (discard) Dispatcher.UIThread.Post(window.ClosePermanently, DispatcherPriority.Background);
    }
    private async Task RetrySelectionAsync(ResultWindow window, CapturePurpose? requestedPurpose = null)
    {
        if (!_windowImages.TryGetValue(window, out var image)) return;
        var purpose = requestedPurpose ?? window.Purpose;
        var operation = BeginOperation(window);
        if (operation is null) return;
        var entered = false;
        try
        {
            // Supersede first; do not let a late previous result overwrite this manual choice.
            window.BeginPurpose(purpose);
            window.SetLoading(CaptureUiText.Preparing, CaptureUiText.LocalProbePrivacy);
            await _operationGate.WaitAsync(operation.Token);
            entered = true;
            EnsureCurrent(operation);
            window.ShowCurrent();
            await ProcessAsync(operation, window, image);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { }
        catch (Exception error) { if (IsCurrent(operation)) window.SetError(UiText.Error(error)); }
        finally { if (entered) _operationGate.Release(); EndOperation(operation); }
    }
    private async Task ProcessAsync(OperationContext operation, ResultWindow window, ImageFrame image)
    {
        switch (window.Purpose)
        {
            case CapturePurpose.Auto: await ProcessAutomaticAsync(operation, window, image); break;
            case CapturePurpose.DecodeQrCode: await ProcessQrCodeAsync(operation, window, image); break;
            case CapturePurpose.TranslateText: await ProcessTextAsync(operation, window, image); break;
            case CapturePurpose.DescribeImage:
            case CapturePurpose.ReconstructPrompt: await ProcessImageAnalysisAsync(operation, window, image); break;
            default: throw new ArgumentOutOfRangeException(nameof(window.Purpose));
        }
    }
    private static async Task<T> WithTimeoutAsync<T>(Func<CancellationToken, Task<T>> action,
        TimeSpan timeout, CancellationToken operationToken, string errorCode, string timeoutMessage)
    {
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
        phase.CancelAfter(timeout);
        try { return await action(phase.Token); }
        catch (OperationCanceledException error) when (!operationToken.IsCancellationRequested)
        { throw new ProviderException(errorCode, timeoutMessage, error); }
    }
    private static string BuildPrivacyDescription(AppSettings settings, ProviderMetadata ocr, ProviderMetadata translation)
    {
        var local = ProcessingModes.IsLocalEndpoint(settings);
        if (ocr.Id == "local-vlm-ocr" && local && (translation.Location == ProviderExecutionLocation.Local || translation.Id == "custom-chat"))
            return UiText.T("图片与文字发送到本机大模型服务 · 内容不离开设备");
        if (ocr.Location == ProviderExecutionLocation.Local && translation.Location == ProviderExecutionLocation.Local)
            return UiText.T("全程本地处理 · 内容不离开设备");
        if (ocr.UploadsImage)
            return UiText.IsEnglish
                ? $"The selected image is sent to {UiText.ProviderName(ocr.Id, ocr.DisplayName)}; text is sent to {UiText.ProviderName(translation.Id, translation.DisplayName)}"
                : $"所选图片将发送给 {ocr.DisplayName}；文字将发送给 {translation.DisplayName}";
        return UiText.IsEnglish
            ? $"The image is recognized locally; text is sent to {UiText.ProviderName(translation.Id, translation.DisplayName)}"
            : $"图片在本地识别；文字将发送给 {translation.DisplayName}";
    }
    private sealed class OperationContext(long id, CancellationTokenSource cancellation)
    {
        public long Id { get; } = id;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public CancellationToken Token => Cancellation.Token;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ResultWindow? TargetWindow { get; set; }
    }
}

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PingYi.Core;
using CorePixelRect = PingYi.Core.PixelRect;

namespace PingYi.App;

public partial class CaptureOverlayWindow : Window
{
    private readonly ImageFrame _capture;
    private readonly Bitmap _bitmap;
    private readonly CaptureOverlaySession? _session;
    private readonly double _preferredScaling;
    private bool _bitmapDisposalQueued;
    private bool _bitmapDisposed;

    public CaptureOverlayWindow() : this(CreatePlaceholderCapture(), 1, session: null)
    {
    }

    internal CaptureOverlayWindow(
        ImageFrame capture,
        double preferredScaling,
        CaptureOverlaySession? session)
    {
        _capture = capture;
        _preferredScaling = preferredScaling > 0 ? preferredScaling : 1;
        _session = session;
        InitializeComponent();
        UiText.Attach(this);
        _bitmap = new Bitmap(new MemoryStream(capture.PngBytes));
        ScreenshotImage.Source = _bitmap;
        Position = new PixelPoint(capture.DesktopBounds.X, capture.DesktopBounds.Y);
        Width = capture.Width / _preferredScaling;
        Height = capture.Height / _preferredScaling;

        Opened += (_, _) =>
        {
            var scale = RenderScaling > 0 ? RenderScaling : _preferredScaling;
            Width = capture.Width / scale;
            Height = capture.Height / scale;
            Position = new PixelPoint(capture.DesktopBounds.X, capture.DesktopBounds.Y);
            _session?.Refresh(this);
            Activate();
            Focus();
        };
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == Key.Escape)
            {
                _session?.Cancel();
            }
        };
        Closed += (_, _) =>
        {
            QueueBitmapDisposal();
            _session?.NotifyClosed();
        };
    }

    internal void ShowOverlay() => Show();

    internal void CloseOverlay()
    {
        Close();
        if (!IsVisible)
        {
            QueueBitmapDisposal();
        }
    }

    internal void SetGlobalSelection(CorePixelRect? selection, bool showSize)
    {
        if (selection is null)
        {
            SelectionOverlay.Selection = null;
            SelectionSizeBorder.IsVisible = false;
            return;
        }

        var visibleSelection = ScreenSelectionGeometry.Intersect(selection.Value, _capture.DesktopBounds);
        if (visibleSelection.IsEmpty)
        {
            SelectionOverlay.Selection = null;
        }
        else
        {
            var scale = RenderScaling > 0 ? RenderScaling : _preferredScaling;
            SelectionOverlay.Selection = new Rect(
                (visibleSelection.X - _capture.DesktopBounds.X) / scale,
                (visibleSelection.Y - _capture.DesktopBounds.Y) / scale,
                visibleSelection.Width / scale,
                visibleSelection.Height / scale);
        }

        SelectionSizeBorder.IsVisible = showSize;
        SelectionSizeText.Text = $"{selection.Value.Width} × {selection.Value.Height} px";
    }

    private void SelectionOverlay_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_session is null ||
            !e.GetCurrentPoint(SelectionOverlay).Properties.IsLeftButtonPressed ||
            !_session.Begin(this, GetGlobalPointer(e.GetPosition(SelectionOverlay))))
        {
            return;
        }

        e.Pointer.Capture(SelectionOverlay);
    }

    private void SelectionOverlay_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        _session?.Move(this, GetGlobalPointer(e.GetPosition(SelectionOverlay)));
    }

    private void SelectionOverlay_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        e.Pointer.Capture(null);
        _session.End(this, GetGlobalPointer(e.GetPosition(SelectionOverlay)));
    }

    private PixelPoint GetGlobalPointer(Point localPosition)
    {
        if (OperatingSystem.IsWindows() && NativePointer.TryGetPosition(out var nativePosition))
        {
            return nativePosition;
        }

        var scale = RenderScaling > 0 ? RenderScaling : _preferredScaling;
        return new PixelPoint(
            Position.X + (int)Math.Round(localPosition.X * scale),
            Position.Y + (int)Math.Round(localPosition.Y * scale));
    }

    private void QueueBitmapDisposal()
    {
        if (_bitmapDisposalQueued)
        {
            return;
        }

        _bitmapDisposalQueued = true;
        // Detach the native bitmap before closing the window, then give Avalonia's
        // render queue a turn to release its reference before disposing the image.
        ScreenshotImage.Source = null;
        Dispatcher.UIThread.Post(DisposeBitmap, DispatcherPriority.Background);
    }

    private void DisposeBitmap()
    {
        if (_bitmapDisposed)
        {
            return;
        }

        _bitmapDisposed = true;
        _bitmap.Dispose();
    }

    private static ImageFrame CreatePlaceholderCapture()
    {
        const string transparentPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        return new ImageFrame(
            Convert.FromBase64String(transparentPng),
            1,
            1,
            new CorePixelRect(0, 0, 1, 1));
    }

    private static class NativePointer
    {
        public static bool TryGetPosition(out PixelPoint position)
        {
            if (GetCursorPos(out var point))
            {
                position = new PixelPoint(point.X, point.Y);
                return true;
            }

            position = default;
            return false;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }
    }
}

internal sealed class CaptureOverlaySession(
    ImageFrame desktop,
    IReadOnlyList<CaptureDisplay> displays,
    IImageCropper imageCropper)
{
    private readonly TaskCompletionSource<CorePixelRect?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<CaptureOverlayWindow> _windows = [];
    private PixelPoint _start;
    private CaptureOverlayWindow? _selectionOwner;
    private CorePixelRect? _selection;
    private bool _selecting;
    private bool _completing;
    private int _openWindowCount;
    private CancellationTokenRegistration _cancellationRegistration;

    public Task<CorePixelRect?> ShowAndSelectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return ShowAndSelectCore(cancellationToken);
        }
        catch
        {
            _completing = true;
            _cancellationRegistration.Dispose();
            CloseWindows();
            throw;
        }
    }

    private Task<CorePixelRect?> ShowAndSelectCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var display in displays)
        {
            var displayCapture = ScreenSelectionGeometry.Intersect(display.Bounds, desktop.DesktopBounds);
            if (displayCapture.IsEmpty)
            {
                continue;
            }

            var relativeCapture = ScreenSelectionGeometry.ToCaptureRelative(
                displayCapture,
                desktop.DesktopBounds);
            var image = imageCropper.Crop(desktop, relativeCapture);
            _windows.Add(new CaptureOverlayWindow(image, display.Scaling, this));
        }

        if (_windows.Count == 0)
        {
            throw new InvalidOperationException("未找到可用于框选的显示器。");
        }

        _openWindowCount = _windows.Count;
        _cancellationRegistration = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(
                () => CompleteCanceled(cancellationToken),
                DispatcherPriority.Send));
        try
        {
            foreach (var window in _windows)
            {
                window.ShowOverlay();
            }
        }
        catch
        {
            _completing = true;
            _cancellationRegistration.Dispose();
            CloseWindows();
            throw;
        }

        return _completion.Task;
    }

    public bool Begin(CaptureOverlayWindow owner, PixelPoint globalPosition)
    {
        if (_completing || _selecting)
        {
            return false;
        }

        _selectionOwner = owner;
        _start = Clamp(globalPosition);
        _selecting = true;
        _selection = new CorePixelRect(_start.X, _start.Y, 0, 0);
        RenderSelection();
        return true;
    }

    public void Move(CaptureOverlayWindow owner, PixelPoint globalPosition)
    {
        if (!_selecting || !ReferenceEquals(owner, _selectionOwner))
        {
            return;
        }

        var current = Clamp(globalPosition);
        _selection = ScreenSelectionGeometry.NormalizeAndClamp(
            _start.X,
            _start.Y,
            current.X,
            current.Y,
            desktop.DesktopBounds);
        RenderSelection();
    }

    public void End(CaptureOverlayWindow owner, PixelPoint globalPosition)
    {
        if (!_selecting || !ReferenceEquals(owner, _selectionOwner))
        {
            return;
        }

        Move(owner, globalPosition);
        _selecting = false;
        var selection = _selection;
        if (selection is null || selection.Value.Width < 8 || selection.Value.Height < 8)
        {
            _selection = null;
            _selectionOwner = null;
            RenderSelection();
            return;
        }

        Complete(ScreenSelectionGeometry.ToCaptureRelative(selection.Value, desktop.DesktopBounds));
    }

    public void Refresh(CaptureOverlayWindow window) =>
        window.SetGlobalSelection(_selection, ReferenceEquals(window, _selectionOwner));

    public void Cancel() => Complete(null);

    public void NotifyClosed()
    {
        _openWindowCount--;
        if (!_completing && _openWindowCount <= 0)
        {
            Complete(null);
        }
    }

    private PixelPoint Clamp(PixelPoint point) =>
        new(
            Math.Clamp(point.X, desktop.DesktopBounds.X, desktop.DesktopBounds.X + desktop.DesktopBounds.Width),
            Math.Clamp(point.Y, desktop.DesktopBounds.Y, desktop.DesktopBounds.Y + desktop.DesktopBounds.Height));

    private void RenderSelection()
    {
        foreach (var window in _windows)
        {
            window.SetGlobalSelection(_selection, ReferenceEquals(window, _selectionOwner));
        }
    }

    private void Complete(CorePixelRect? selection)
    {
        if (_completing)
        {
            return;
        }

        _completing = true;
        _cancellationRegistration.Dispose();
        CloseWindows();
        _completion.TrySetResult(selection);
    }

    private void CompleteCanceled(CancellationToken cancellationToken)
    {
        if (_completing)
        {
            return;
        }

        _completing = true;
        _cancellationRegistration.Dispose();
        CloseWindows();
        _completion.TrySetCanceled(cancellationToken);
    }

    private void CloseWindows()
    {
        foreach (var window in _windows)
        {
            try
            {
                window.CloseOverlay();
            }
            catch
            {
                // A native overlay may already have closed; always clean up the rest.
            }
        }
    }
}

public sealed class SelectionOverlayControl : Control
{
    private Rect? _selection;

    public Rect? Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var shade = new SolidColorBrush(Color.FromArgb(145, 4, 10, 22));
        if (Selection is not { } selection || selection.Width <= 0 || selection.Height <= 0)
        {
            context.FillRectangle(shade, Bounds);
            return;
        }

        context.FillRectangle(shade, new Rect(0, 0, Bounds.Width, Math.Max(0, selection.Top)));
        context.FillRectangle(shade, new Rect(0, selection.Bottom, Bounds.Width, Math.Max(0, Bounds.Height - selection.Bottom)));
        context.FillRectangle(shade, new Rect(0, selection.Top, Math.Max(0, selection.Left), selection.Height));
        context.FillRectangle(shade, new Rect(selection.Right, selection.Top, Math.Max(0, Bounds.Width - selection.Right), selection.Height));
        var accent = new SolidColorBrush(Color.FromRgb(45, 212, 191));
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(14, 45, 212, 191)), new Pen(accent, 2), selection);

        const double handleSize = 8;
        var halfHandle = handleSize / 2;
        var handleFill = new SolidColorBrush(Colors.White);
        foreach (var point in new[]
                 {
                     selection.TopLeft,
                     selection.TopRight,
                     selection.BottomLeft,
                     selection.BottomRight
                 })
        {
            var handle = new Rect(point.X - halfHandle, point.Y - halfHandle, handleSize, handleSize);
            context.DrawRectangle(handleFill, new Pen(accent, 2), handle, 2, 2);
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using Avalonia.Threading;
using PingYi.Infrastructure;

namespace PingYi.App;

/// <summary>Temporarily conceals all native app surfaces, preserving modal tasks and ownership.</summary>
internal sealed class CaptureVisibilityScope : IDisposable
{
    private sealed record Surface(Window Window, IWindowBaseImpl Platform, int Depth, bool Dialog, IDisposable Transitions);
    private static int _active;
    private readonly List<Surface> _windows = [];
    private readonly HashSet<Window> _closed = [];
    private readonly HashSet<Window> _concealed = [];
    private bool _disposed;
    private readonly Action<Window, bool> _nativeVisibility;
    internal static bool IsActive => Volatile.Read(ref _active) > 0;
    internal bool IsConcealed(Window window) => _concealed.Contains(window);

    internal static CaptureVisibilityScope HideAll(Window? fallback = null)
    {
        var windows = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.ToArray()
            : fallback is null ? [] : new[] { fallback };
        return new CaptureVisibilityScope(windows);
    }

    internal CaptureVisibilityScope(IEnumerable<Window> windows, Action<Window, bool>? nativeVisibility = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _nativeVisibility = nativeVisibility ?? ((window, visible) =>
        {
            var handle = window.TryGetPlatformHandle()
                ?? throw new PlatformNotSupportedException("A native capture window handle is required.");
            DesktopComposition.SetNativeWindowVisibility(handle.Handle, handle.HandleDescriptor, visible);
        });
        Interlocked.Increment(ref _active);
        try
        {
            foreach (var window in windows.Where(w => w.IsVisible && w.WindowState != WindowState.Minimized &&
                         w is not CaptureOverlayWindow).Distinct())
            {
                if (window.PlatformImpl is not { } platform) continue;
                window.Closed += OnClosed;
                var handle = window.TryGetPlatformHandle();
                _windows.Add(new Surface(window, platform, OwnerDepth(window), DialogPresentation.IsDialog(window),
                    DesktopComposition.SuppressTransitions(handle?.HandleDescriptor == "HWND" ? handle.Handle : IntPtr.Zero)));
                ClosePopups(window);
            }
            foreach (var item in _windows.OrderByDescending(item => item.Depth))
            {
                // Window.Hide() clears Owner AND completes ShowDialog in Avalonia 12.1.
                // Suspend only the native surface instead. Logical state and pending modal tasks
                // remain intact, and restoring does not fire Opened or reload models/credentials.
                _nativeVisibility(item.Window, false);
                _concealed.Add(item.Window);
            }
        }
        catch { Restore(showWindows: true); throw; }
    }

    internal async Task WaitForDesktopAsync(CancellationToken token, Func<CancellationToken, Task>? barrier = null)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await (barrier ?? DesktopComposition.WaitForFrameAsync)(token);
        token.ThrowIfCancellationRequested();
        foreach (var item in _windows)
        {
            if (_closed.Contains(item.Window)) continue;
            var handle = item.Window.TryGetPlatformHandle();
            if (handle?.HandleDescriptor == "HWND" && DesktopComposition.IsNativeWindowVisible(handle.Handle))
                throw new InvalidOperationException(UiText.T("截图准备期间窗口重新出现，请重试。"));
        }
    }

    private static void ClosePopups(Window window)
    {
        foreach (var control in window.GetLogicalDescendants().OfType<Control>().Prepend(window))
        {
            if (control is ComboBox combo) combo.IsDropDownOpen = false;
            if (control is Button button) button.Flyout?.Hide();
            control.ContextMenu?.Close();
        }
    }

    private static int OwnerDepth(Window window)
    {
        var depth = 0;
        for (var owner = window.Owner; owner is not null; owner = owner.Owner) depth++;
        return depth;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is Window window) _closed.Add(window);
    }

    internal void Restore(bool showWindows)
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            foreach (var item in _windows.OrderBy(item => item.Depth))
            {
                try
                {
                    // Closed/explicitly hidden windows must not be resurrected. Always attempt
                    // remaining surfaces even if one native window disappeared during capture.
                    if (showWindows && !_closed.Contains(item.Window) && item.Window.IsVisible &&
                        ReferenceEquals(item.Window.PlatformImpl, item.Platform))
                        _nativeVisibility(item.Window, true);
                }
                catch (ObjectDisposedException) { }
                finally
                {
                    _concealed.Remove(item.Window);
                    item.Window.Closed -= OnClosed;
                    item.Transitions.Dispose();
                }
            }
        }
        finally { Interlocked.Decrement(ref _active); }
    }

    public void Dispose() => Restore(showWindows: true);
}

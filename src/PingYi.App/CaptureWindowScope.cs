using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PingYi.App;

/// <summary>Isolate all visible application windows for one capture/selection transaction.</summary>
internal static class CaptureWindowScope
{
    public static async Task<T> RunAsync<T>(IEnumerable<Window> windows,
        Func<CancellationToken, Task> waitForDesktop,
        Func<CancellationToken, Task<T>> captureAndSelect,
        CancellationToken cancellationToken,
        Func<bool>? canRestore = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        var snapshots = windows.Distinct().Where(w => w.IsVisible && w.WindowState != WindowState.Minimized)
            .Select(w => new Snapshot(w)).ToArray();
        try
        {
            foreach (var snapshot in snapshots) snapshot.Hide();
            await waitForDesktop(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return await captureAndSelect(cancellationToken);
        }
        finally
        {
            // A superseding capture waits on the coordinator gate until this restoration finishes.
            // Cancellation, Esc, failures, and successful selection all use the same cleanup path.
            foreach (var snapshot in snapshots) snapshot.Restore(canRestore?.Invoke() ?? true);
        }
    }

    private sealed class Snapshot
    {
        private readonly Window _window;
        private readonly WindowState _state;
        private readonly Avalonia.PixelPoint _position;
        private IDisposable? _nativeState;
        private bool _closed;
        private bool _hidden;
        public Snapshot(Window window)
        {
            _window = window;
            _state = window.WindowState;
            _position = window.Position;
            _window.Closed += OnClosed;
        }
        private void OnClosed(object? sender, EventArgs e) => _closed = true;
        public void Hide()
        {
            // Popups can have their own native windows. Close them before hiding their owners.
            foreach (var combo in _window.GetLogicalDescendants().OfType<ComboBox>()
                         .Concat(_window.GetVisualDescendants().OfType<ComboBox>()).Distinct())
                combo.IsDropDownOpen = false;
            _nativeState = NativeCaptureState.Apply(_window);
            _hidden = true;
            _window.Hide();
        }
        public void Restore(bool visible)
        {
            try
            {
                _nativeState?.Dispose();
                if (visible && _hidden && !_closed)
                {
                    _window.WindowState = _state;
                    _window.Position = _position;
                    _window.Show();
                }
            }
            catch (InvalidOperationException) when (_closed) { }
            finally { _window.Closed -= OnClosed; }
        }
    }
}

internal static class DesktopCaptureBarrier
{
    public static async Task WaitAsync(CancellationToken cancellationToken)
    {
        // Drain queued Avalonia/native hide work before asking the OS for a desktop frame.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(OperatingSystem.IsWindows() ? 120 : 200, cancellationToken);
        if (OperatingSystem.IsWindows())
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                // DwmFlush is an additional presentation barrier, not a claim that every
                // compositor in the session has flushed. Hide + transition suppression remain required.
                try { _ = DwmFlush(); }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            }, cancellationToken);
            await Task.Delay(34, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}

internal sealed class NativeCaptureState : IDisposable
{
    private const int TransitionsForceDisabled = 3;
    private readonly IntPtr _handle;
    private int _previousTransitions;
    private uint _previousAffinity;
    private bool _restoreTransitions;
    private bool _restoreAffinity;
    private NativeCaptureState(IntPtr handle) => _handle = handle;

    public static IDisposable? Apply(Window window)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var handle = window.TryGetPlatformHandle();
        if (handle?.HandleDescriptor != "HWND" || handle.Handle == IntPtr.Zero) return null;
        var state = new NativeCaptureState(handle.Handle);
        // Only our own windows, only during this operation. Do not globally prevent users
        // from taking screenshots of PingYi or change display-affinity settings permanently.
        if (DwmGetWindowAttribute(state._handle, TransitionsForceDisabled, out state._previousTransitions, 4) >= 0)
        {
            var disabled = 1;
            state._restoreTransitions = DwmSetWindowAttribute(state._handle, TransitionsForceDisabled, ref disabled, 4) >= 0;
        }
        // On earlier Windows builds 0x11 means black content, not exclusion. Never use it there.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
            GetWindowDisplayAffinity(state._handle, out state._previousAffinity))
            state._restoreAffinity = SetWindowDisplayAffinity(state._handle, 0x11);
        return state;
    }
    public void Dispose()
    {
        if (_restoreAffinity) { _ = SetWindowDisplayAffinity(_handle, _previousAffinity); _restoreAffinity = false; }
        if (_restoreTransitions)
        {
            _ = DwmSetWindowAttribute(_handle, TransitionsForceDisabled, ref _previousTransitions, 4);
            _restoreTransitions = false;
        }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}

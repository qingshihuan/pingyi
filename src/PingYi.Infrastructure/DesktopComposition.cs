using System.Runtime.InteropServices;

namespace PingYi.Infrastructure;

/// <summary>Short-lived capture preparation; no display-affinity changes or permanent animation changes.</summary>
public static class DesktopComposition
{
    private const uint TransitionsForceDisabled = 3;

    public static IDisposable SuppressTransitions(IntPtr window)
    {
        if (!OperatingSystem.IsWindows() || window == IntPtr.Zero) return new TransitionLease(IntPtr.Zero);
        var disabled = 1;
        var result = DwmSetWindowAttribute(window, TransitionsForceDisabled, ref disabled, sizeof(int));
        return new TransitionLease(result >= 0 ? window : IntPtr.Zero);
    }

    public static async Task WaitForFrameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            // DwmFlush synchronizes this application's pending surface updates; it is not
            // a promise to flush every other application's rendering on every monitor.
            var flushed = await Task.Run(() => DwmFlush() >= 0, cancellationToken).WaitAsync(cancellationToken);
            await Task.Delay(flushed ? 40 : 250, cancellationToken);
            if (flushed) await Task.Run(() => DwmFlush(), cancellationToken).WaitAsync(cancellationToken);
        }
        else
        {
            // X11 unmap/repaint is asynchronous. Preserve a bounded compositor grace period.
            await Task.Delay(180, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    // Called on the UI thread for this application's own windows only. Unlike
    // Window.Hide(), native concealment does not end an Avalonia modal dialog.
    public static void SetNativeWindowVisibility(IntPtr window, string? descriptor, bool visible)
    {
        if (window == IntPtr.Zero) throw new ArgumentException("Invalid native window.", nameof(window));
        if (OperatingSystem.IsWindows() && descriptor == "HWND")
        {
            // SW_SHOWNA preserves the current size/state without activating the window.
            _ = ShowWindow(window, visible ? 8 : 0);
            return;
        }
        if (OperatingSystem.IsLinux() && descriptor == "XID")
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) throw new InvalidOperationException("Cannot connect to the X11 display.");
            try
            {
                if (visible) _ = XMapWindow(display, window);
                else _ = XUnmapWindow(display, window);
                _ = XSync(display, false);
            }
            finally { _ = XCloseDisplay(display); }
            return;
        }
        throw new PlatformNotSupportedException("Native capture concealment requires Windows or X11.");
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XMapWindow(IntPtr display, IntPtr window);
    [DllImport("libX11.so.6")] private static extern int XUnmapWindow(IntPtr display, IntPtr window);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, [MarshalAs(UnmanagedType.Bool)] bool discard);

    public static bool IsNativeWindowVisible(IntPtr window) =>
        OperatingSystem.IsWindows() && window != IntPtr.Zero && IsWindowVisible(window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    private sealed class TransitionLease(IntPtr window) : IDisposable
    {
        private IntPtr _window = window;
        public void Dispose()
        {
            var handle = Interlocked.Exchange(ref _window, IntPtr.Zero);
            if (handle == IntPtr.Zero || !OperatingSystem.IsWindows()) return;
            var disabled = 0;
            _ = DwmSetWindowAttribute(handle, TransitionsForceDisabled, ref disabled, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmFlush();

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref int value, int size);
}

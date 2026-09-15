from pathlib import Path
root=Path(__file__).resolve().parent.parent
app=root/'src/PingYi.App'
p=app/'CaptureVisibilityScope.cs'
s=p.read_text(encoding='utf-8')
s=s.replace('    private bool _disposed;', '    private bool _disposed;\n    private readonly Action<Window, bool> _nativeVisibility;')
s=s.replace('internal CaptureVisibilityScope(IEnumerable<Window> windows)', 'internal CaptureVisibilityScope(IEnumerable<Window> windows, Action<Window, bool>? nativeVisibility = null)')
s=s.replace('        Dispatcher.UIThread.VerifyAccess();', '''        Dispatcher.UIThread.VerifyAccess();
        _nativeVisibility = nativeVisibility ?? ((window, visible) =>
        {
            var handle = window.TryGetPlatformHandle()
                ?? throw new PlatformNotSupportedException("A native capture window handle is required.");
            DesktopComposition.SetNativeWindowVisibility(handle.Handle, handle.HandleDescriptor, visible);
        });''')
s=s.replace('item.Platform.Hide();', '_nativeVisibility(item.Window, false);')
s=s.replace('item.Platform.Show(activate: false, isDialog: item.Dialog);', '_nativeVisibility(item.Window, true);')
p.write_text(s,encoding='utf-8',newline='\n')
p=root/'src/PingYi.Infrastructure/DesktopComposition.cs'
s=p.read_text(encoding='utf-8').replace('    public static bool IsNativeWindowVisible', '''    // Called on the UI thread for this application's own windows only. Unlike
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

    public static bool IsNativeWindowVisible''')
p.write_text(s,encoding='utf-8',newline='\n')
p=root/'tests/PingYi.App.Tests/RemediationTests.cs'
s=p.read_text(encoding='utf-8')
s=s.replace('new Window[] { main, settings, pinned, hidden }))', 'new Window[] { main, settings, pinned, hidden }, nativeVisibility: (_, _) => { }))')
s=s.replace('new[] { main, settings, pinned }))', 'new[] { main, settings, pinned }, nativeVisibility: (_, _) => { }))')
p.write_text(s,encoding='utf-8',newline='\n')

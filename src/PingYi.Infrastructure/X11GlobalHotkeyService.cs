using System.Runtime.InteropServices;
using PingYi.Core;

namespace PingYi.Infrastructure;

// One connection, owned by one worker. The asynchronous XGrabKey error must be
// collected at XSync; its integer return value does not report BadAccess.
public sealed class X11GlobalHotkeyService : IGlobalHotkeyService
{
    private static readonly object ErrorHandlerGate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _stop;
    private Task? _worker;
    public event EventHandler? Pressed;

    public async Task StartAsync(string shortcut, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_worker is { IsCompleted: false }) return;
            if (LinuxDesktop.Session == LinuxDesktopSession.Wayland)
                throw new ProviderException("hotkey_wayland", "Wayland 由桌面管理全局快捷键，请在系统设置中配置截图命令。");
            var gesture = GlobalHotkeyGesture.Parse(shortcut);
            _stop?.Dispose();
            _stop = new CancellationTokenSource();
            var stop = _stop;
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _worker = Task.Factory.StartNew(() => Run(gesture, started, stop.Token),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            try { await started.Task.WaitAsync(cancellationToken); }
            catch
            {
                stop.Cancel();
                await _worker; // Worker owns all native cleanup, even when startup is cancelled.
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            _stop?.Cancel();
            if (_worker is not null) await _worker;
            _worker = null;
            _stop?.Dispose();
            _stop = null;
        }
        finally { _lifecycle.Release(); }
    }

    private void Run(GlobalHotkeyGesture gesture, TaskCompletionSource started, CancellationToken token)
    {
        IntPtr display = IntPtr.Zero, root = IntPtr.Zero;
        uint[] masks = [];
        var key = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
                throw new ProviderException("hotkey_display_unavailable", "无法连接桌面显示服务器；仍可使用截图按钮。");
            root = XDefaultRootWindow(display);
            key = XKeysymToKeycode(display, XStringToKeysym(char.ToLowerInvariant(gesture.Key).ToString()));
            // Keycode 0 means AnyKey to XGrabKey. Never register it.
            if (key == 0) throw new ProviderException("hotkey_key_unmapped", "当前键盘布局中找不到该快捷键，请更换按键。");
            uint modifiers = (gesture.Shift ? 1u : 0) | (gesture.Control ? 4u : 0) | (gesture.Alt ? 8u : 0);
            masks = LockCombinations(display).Select(mask => modifiers | mask).Distinct().ToArray();
            var error = Grab(display, root, key, masks);
            if (error != 0)
                throw new ProviderException(error == 10 ? "hotkey_conflict" : "hotkey_registration_failed",
                    error == 10 ? "快捷键已被系统或其他程序占用。请在设置中更换组合；截图按钮仍可使用。" : "全局快捷键注册失败；截图按钮仍可使用。");
            token.ThrowIfCancellationRequested();
            started.TrySetResult();
            while (!token.IsCancellationRequested)
            {
                while (!token.IsCancellationRequested && XPending(display) > 0)
                {
                    XNextEvent(display, out var e);
                    if (e.Type == 2 && e.Keycode == key)
                        Pressed?.Invoke(this, EventArgs.Empty);
                }
                token.WaitHandle.WaitOne(20);
            }
        }
        catch (Exception e) { started.TrySetException(e); }
        finally
        {
            if (display != IntPtr.Zero)
            {
                // Roll back partial grabs too. Never ungrab another application's connection.
                foreach (var mask in masks) XUngrabKey(display, key, mask, root);
                XSync(display, false);
                XCloseDisplay(display);
            }
        }
    }

    private static uint[] LockCombinations(IntPtr display)
    {
        uint ignored = 2; // Caps Lock
        var pointer = XGetModifierMapping(display);
        if (pointer != IntPtr.Zero)
        {
            try
            {
                var mapping = Marshal.PtrToStructure<XModifierKeymap>(pointer);
                var num = XKeysymToKeycode(display, XStringToKeysym("Num_Lock"));
                var scroll = XKeysymToKeycode(display, XStringToKeysym("Scroll_Lock"));
                for (var modifier = 0; modifier < 8; modifier++)
                    for (var slot = 0; slot < mapping.MaxKeys; slot++)
                    {
                        var code = Marshal.ReadByte(mapping.Keys, modifier * mapping.MaxKeys + slot);
                        if (code != 0 && (code == num || code == scroll)) ignored |= 1u << modifier;
                    }
            }
            finally { XFreeModifiermap(pointer); }
        }
        var combinations = new List<uint> { 0 };
        for (var bit = 0; bit < 8; bit++)
            if ((ignored & (1u << bit)) != 0)
                combinations.AddRange(combinations.ToArray().Select(value => value | (1u << bit)));
        return combinations.ToArray();
    }

    private static unsafe byte Grab(IntPtr display, IntPtr root, int key, uint[] masks)
    {
        lock (ErrorHandlerGate)
        {
            byte error = 0;
            IntPtr previous = IntPtr.Zero;
            XErrorHandler handler = (connection, pointer) =>
            {
                var data = Marshal.PtrToStructure<XErrorEvent>(pointer);
                if (connection == display && data.RequestCode == 33) // X_GrabKey
                { error = data.ErrorCode; return 0; }
                // XSetErrorHandler is process-wide: keep Avalonia's handler for all
                // other connections/errors and restore it as soon as our XSync completes.
                return previous == IntPtr.Zero ? 0 : ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)previous)(connection, pointer);
            };
            previous = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(handler));
            try
            {
                foreach (var mask in masks) XGrabKey(display, key, mask, root, false, 1, 1);
                XSync(display, false);
            }
            finally { XSetErrorHandler(previous); GC.KeepAlive(handler); }
            return error;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int XErrorHandler(IntPtr display, IntPtr error);
    [StructLayout(LayoutKind.Sequential)] private struct XModifierKeymap { public int MaxKeys; public IntPtr Keys; }
    [StructLayout(LayoutKind.Sequential)] private struct XErrorEvent
    {
        public int Type; public IntPtr Display; public nuint ResourceId, Serial;
        public byte ErrorCode, RequestCode, MinorCode;
    }
    // XEvent is 24 native longs; keycode follows state in XKeyEvent on Linux x64.
    [StructLayout(LayoutKind.Explicit, Size = 192)] private struct XEvent
    { [FieldOffset(0)] public int Type; [FieldOffset(84)] public uint Keycode; }
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern nuint XStringToKeysym(string value);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, nuint symbol);
    [DllImport("libX11.so.6")] private static extern IntPtr XGetModifierMapping(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XFreeModifiermap(IntPtr pointer);
    [DllImport("libX11.so.6")] private static extern IntPtr XSetErrorHandler(IntPtr handler);
    [DllImport("libX11.so.6")] private static extern int XGrabKey(IntPtr display, int key, uint modifiers, IntPtr root, [MarshalAs(UnmanagedType.Bool)] bool owner, int pointerMode, int keyboardMode);
    [DllImport("libX11.so.6")] private static extern int XUngrabKey(IntPtr display, int key, uint modifiers, IntPtr root);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, [MarshalAs(UnmanagedType.Bool)] bool discard);
    [DllImport("libX11.so.6")] private static extern int XPending(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(IntPtr display, out XEvent e);
}

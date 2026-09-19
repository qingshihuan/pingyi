using System.Runtime.InteropServices;
using PingYi.Core;

namespace PingYi.Infrastructure;

// A private X connection, accessed on one dedicated thread. Never let BadAccess reach Xlib's
// default error handler (which terminates the process). Other clients' errors are forwarded.
internal sealed class X11GlobalHotkeyService : IGlobalHotkeyService
{
    private static readonly object ErrorHandlerGate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private Thread? _thread;
    private volatile bool _stopping;
    public X11GlobalHotkeyService() => X11Threading.EnsureInitialized();
    public event EventHandler? Pressed;

    public async Task StartAsync(string shortcut, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_thread?.IsAlive == true) return;
            cancellationToken.ThrowIfCancellationRequested();
            var gesture = GlobalHotkeyGesture.Parse(shortcut);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopping = false;
            _thread = new Thread(() => EventLoop(gesture, started))
                { IsBackground = true, Name = "PingYi.X11GlobalHotkey" };
            _thread.Start();
            try { await started.Task.WaitAsync(cancellationToken); }
            catch { await StopThreadAsync(); throw; }
        }
        finally { _lifecycle.Release(); }
    }
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try { await StopThreadAsync(); }
        finally { _lifecycle.Release(); }
    }
    private async Task StopThreadAsync()
    {
        _stopping = true;
        var thread = _thread;
        if (thread is null) return;
        // Joining on the UI thread would prevent clean cancellation and frozen-server recovery.
        if (!await Task.Run(() => thread.Join(TimeSpan.FromSeconds(2))))
            throw new InvalidOperationException("X11 快捷键线程未能停止，请退出屏译后重试。");
        _thread = null;
    }
    private void EventLoop(GlobalHotkeyGesture gesture, TaskCompletionSource started)
    {
        IntPtr display = IntPtr.Zero;
        IntPtr root = IntPtr.Zero;
        int key = 0;
        uint[] masks = [];
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) throw new InvalidOperationException("无法连接 X11 显示服务器。");
            root = XDefaultRootWindow(display);
            key = XKeysymToKeycode(display, XStringToKeysym(char.ToLowerInvariant(gesture.Key).ToString()));
            if (key == 0) throw new NotSupportedException("当前键盘布局不支持此快捷键。");
            var modifiers = (gesture.Shift ? 1u : 0) | (gesture.Control ? 4u : 0) | (gesture.Alt ? 8u : 0);
            // NumLock need not be Mod2 on a customized keyboard.
            var numLock = FindNumLockMask(display);
            masks = new[] { modifiers, modifiers | 2, modifiers | numLock, modifiers | 2 | numLock }.Distinct().ToArray();
            var error = GrabChecked(display, root, key, masks);
            if (error != 0) throw new ProviderException("hotkey_conflict", "快捷键已被系统或其他程序占用，请更换快捷键；截图按钮仍可使用。");
            if (_stopping) { started.TrySetCanceled(); return; }
            started.TrySetResult();
            while (!_stopping)
            {
                while (!_stopping && XPending(display) > 0)
                {
                    XNextEvent(display, out var e);
                    if (e.Type == 2 && e.KeyCode == key && (e.State & ~(2u | numLock)) == modifiers)
                        Pressed?.Invoke(this, EventArgs.Empty);
                }
                Thread.Sleep(20);
            }
        }
        catch (Exception exception) { started.TrySetException(exception); }
        finally
        {
            if (display != IntPtr.Zero)
            {
                // Undo partial success too. This cannot remove another client's grab.
                foreach (var mask in masks) XUngrabKey(display, key, mask, root);
                XSync(display, false);
                XCloseDisplay(display);
            }
        }
    }
    private static byte GrabChecked(IntPtr display, IntPtr root, int key, uint[] masks)
    {
        lock (ErrorHandlerGate)
        {
            XSync(display, false);
            byte error = 0;
            IntPtr previous = IntPtr.Zero;
            ErrorHandler handler = (d, p) =>
            {
                var e = Marshal.PtrToStructure<XErrorEvent>(p);
                if (d == display && e.RequestCode == 33) { error = e.ErrorCode; return 0; }
                return previous == IntPtr.Zero ? 0 : Marshal.GetDelegateForFunctionPointer<ErrorHandler>(previous)(d, p);
            };
            previous = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(handler));
            try
            {
                foreach (var mask in masks) XGrabKey(display, key, mask, root, false, 1, 1);
                XSync(display, false); // registration is asynchronous; its return value is not success.
            }
            finally { XSetErrorHandler(previous); GC.KeepAlive(handler); }
            return error;
        }
    }
    private static uint FindNumLockMask(IntPtr display)
    {
        var key = XKeysymToKeycode(display, XStringToKeysym("Num_Lock"));
        var pointer = XGetModifierMapping(display);
        if (pointer == IntPtr.Zero) return 0;
        try
        {
            var map = Marshal.PtrToStructure<ModifierMap>(pointer);
            for (int modifier = 0; modifier < 8; modifier++)
                for (int i = 0; i < map.MaxKeys; i++)
                    if (key != 0 && Marshal.ReadByte(map.Keys, modifier * map.MaxKeys + i) == key)
                        return 1u << modifier;
            return 0;
        }
        finally { XFreeModifiermap(pointer); }
    }
    public async ValueTask DisposeAsync() => await StopAsync();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ErrorHandler(IntPtr display, IntPtr error);
    [StructLayout(LayoutKind.Sequential)] private struct ModifierMap { public int MaxKeys; public IntPtr Keys; }
    [StructLayout(LayoutKind.Sequential)] private struct XErrorEvent
    {
        public int Type; public IntPtr Display; public nuint ResourceId; public nuint Serial;
        public byte ErrorCode; public byte RequestCode; public byte MinorCode;
    }
    // XKeyEvent on supported Linux x64, inside the 24-long XEvent union.
    [StructLayout(LayoutKind.Explicit, Size = 192)] private struct XEvent
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(80)] public uint State;
        [FieldOffset(84)] public uint KeyCode;
    }
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XStringToKeysym(string name);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);
    [DllImport("libX11.so.6")] private static extern int XGrabKey(IntPtr display, int key, uint modifiers, IntPtr window,
        [MarshalAs(UnmanagedType.Bool)] bool ownerEvents, int pointerMode, int keyboardMode);
    [DllImport("libX11.so.6")] private static extern int XUngrabKey(IntPtr display, int key, uint modifiers, IntPtr window);
    [DllImport("libX11.so.6")] private static extern int XPending(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(IntPtr display, out XEvent e);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, [MarshalAs(UnmanagedType.Bool)] bool discard);
    [DllImport("libX11.so.6")] private static extern IntPtr XSetErrorHandler(IntPtr handler);
    [DllImport("libX11.so.6")] private static extern IntPtr XGetModifierMapping(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XFreeModifiermap(IntPtr map);
}

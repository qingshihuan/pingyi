using System.ComponentModel;
using System.Runtime.InteropServices;
using PingYi.Core;

namespace PingYi.Infrastructure;

public readonly record struct GlobalHotkeyGesture(bool Control, bool Alt, bool Shift, char Key)
{
    public static GlobalHotkeyGesture Parse(string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            throw new NotSupportedException("快捷键不能为空。");
        }

        var control = false;
        var alt = false;
        var shift = false;
        char? key = null;
        foreach (var token in shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                control = true;
            }
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                alt = true;
            }
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                shift = true;
            }
            else if (token.Length == 1 && char.IsAsciiLetterOrDigit(token[0]) && key is null)
            {
                key = char.ToUpperInvariant(token[0]);
            }
            else
            {
                throw new NotSupportedException($"不支持的快捷键：{shortcut}。");
            }
        }

        if (key is null || (!control && !alt && !shift))
        {
            throw new NotSupportedException("快捷键必须包含 Ctrl、Alt 或 Shift，以及一个字母或数字。");
        }

        return new GlobalHotkeyGesture(control, alt, shift, key.Value);
    }
}

public static class GlobalHotkeyServiceFactory
{
    public static IGlobalHotkeyService Create() =>
        new HotkeyRegistrationService(OperatingSystem.IsWindows()
            ? new WindowsGlobalHotkeyService()
            : LinuxDesktop.Session == LinuxDesktopSession.Wayland
                ? new PortalGlobalHotkeyService()
                : new X11GlobalHotkeyService());
}

internal sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const int WmHotkey = 0x0312;
    private const int WmQuit = 0x0012;
    private const uint ModAlt = 0x0001;
    private const uint ModShift = 0x0004;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const int HotkeyId = 0x5049;

    private Thread? _thread;
    private uint _threadId;

    public event EventHandler? Pressed;

    public async Task StartAsync(string shortcut, CancellationToken cancellationToken = default)
    {
        if (_thread is not null)
        {
            return;
        }

        var gesture = GlobalHotkeyGesture.Parse(shortcut);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => MessageLoop(gesture, shortcut, started))
        {
            IsBackground = true,
            Name = "PingYi.GlobalHotkey"
        };
        _thread.Start();
        try
        {
            await started.Task.WaitAsync(cancellationToken);
        }
        catch
        {
            _thread = null;
            _threadId = 0;
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_thread is null)
        {
            return Task.CompletedTask;
        }

        PostThreadMessage(_threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _threadId = 0;
        return Task.CompletedTask;
    }

    private void MessageLoop(GlobalHotkeyGesture gesture, string shortcut, TaskCompletionSource started)
    {
        _threadId = GetCurrentThreadId();
        var modifiers = ModNoRepeat;
        if (gesture.Control) modifiers |= ModControl;
        if (gesture.Alt) modifiers |= ModAlt;
        if (gesture.Shift) modifiers |= ModShift;
        if (!RegisterHotKey(IntPtr.Zero, HotkeyId, modifiers, gesture.Key))
        {
            started.SetException(new Win32Exception(Marshal.GetLastWin32Error(), $"快捷键 {shortcut} 已被其他程序占用。"));
            return;
        }

        started.SetResult();
        try
        {
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.MessageId == WmHotkey && message.WParam.ToInt32() == HotkeyId)
                {
                    Pressed?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr HWnd;
        public uint MessageId;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr window, uint minimum, uint maximum);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}

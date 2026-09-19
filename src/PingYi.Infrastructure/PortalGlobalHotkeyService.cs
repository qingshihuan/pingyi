using PingYi.Core;

namespace PingYi.Infrastructure;

/// <summary>Use compositor-approved shortcuts; never XGrabKey under XWayland.</summary>
public sealed class PortalGlobalHotkeyService : IGlobalHotkeyService, IGlobalHotkeyStatus
{
    private const string Interface = "org.freedesktop.portal.GlobalShortcuts";
    private CancellationTokenSource? _stop;
    private Task? _worker;
    public bool IsRegistered { get; private set; }
    public string? RegisteredShortcut { get; private set; }
    public Exception? RegistrationError { get; private set; }
    public event EventHandler? RegistrationChanged;
    public event EventHandler? Pressed;

    public async Task StartAsync(string shortcut, CancellationToken cancellationToken = default)
    {
        if (_worker is { IsCompleted: false }) return;
        var gesture = GlobalHotkeyGesture.Parse(shortcut);
        _stop?.Dispose();
        _stop = new CancellationTokenSource();
        var stop = _stop;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _worker = Task.Factory.StartNew(() => Run(gesture, started, stop.Token), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try { await started.Task.WaitAsync(cancellationToken); }
        catch { stop.Cancel(); await _worker; throw; }
    }

    private void Run(GlobalHotkeyGesture gesture, TaskCompletionSource started, CancellationToken stop)
    {
        GioPortal? portal = null;
        string? session = null;
        try
        {
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(stop);
            startup.CancelAfter(TimeSpan.FromMinutes(2));
            portal = new GioPortal(startup.Token);
            portal.RegisterApplication(startup.Token);
            var requestToken = "pingyi_" + Guid.NewGuid().ToString("N");
            var sessionToken = "keys_" + Guid.NewGuid().ToString("N");
            using (var created = portal.Request(Interface, "CreateSession", requestToken,
                $"({{'handle_token': <'{requestToken}'>, 'session_handle_token': <'{sessionToken}'>}},)", startup.Token))
            {
                if (created.Code != 0) throw Unavailable();
                using var handle = created.Values.Lookup("session_handle");
                session = handle?.String();
                if (session is null || !session.StartsWith(GioPortal.DesktopPath + "/session/", StringComparison.Ordinal))
                    throw Unavailable();
            }
            var closed = false;
            portal.Subscribe("org.freedesktop.portal.Session", "Closed", session, (_, _) => closed = true);
            portal.Subscribe(Interface, "Activated", GioPortal.DesktopPath, (_, value) =>
            {
                if (value.Type != "(osta{sv})") return;
                using var handle = value.Child(0);
                using var id = value.Child(1);
                if (handle.String() == session && id.String() == "capture" && IsRegistered)
                    Pressed?.Invoke(this, EventArgs.Empty);
            });
            portal.Subscribe(Interface, "ShortcutsChanged", GioPortal.DesktopPath, (_, value) =>
            {
                if (value.Type != "(oa(sa{sv}))") return;
                using var handle = value.Child(0);
                if (handle.String() != session) return;
                using var shortcuts = value.Child(1);
                UpdateShortcut(shortcuts);
                RegistrationChanged?.Invoke(this, EventArgs.Empty);
            });
            requestToken = "bind_" + Guid.NewGuid().ToString("N");
            var trigger = (gesture.Control ? "CTRL+" : "") + (gesture.Alt ? "ALT+" : "") +
                (gesture.Shift ? "SHIFT+" : "") + char.ToLowerInvariant(gesture.Key);
            using (var bound = portal.Request(Interface, "BindShortcuts", requestToken,
                $"(objectpath {GioPortal.Quote(session)}, [('capture', {{'description': <'PingYi screenshot translation'>, 'preferred_trigger': <{GioPortal.Quote(trigger)}>}})], '', {{'handle_token': <'{requestToken}'>}})", startup.Token))
            {
                if (bound.Code != 0) throw Unavailable();
                using var shortcuts = bound.Values.Lookup("shortcuts");
                if (shortcuts is null) throw Unavailable();
                UpdateShortcut(shortcuts);
                if (!IsRegistered) throw Unavailable();
            }
            started.TrySetResult();
            RegistrationChanged?.Invoke(this, EventArgs.Empty);
            portal.PumpUntil(() => closed, stop);
            if (closed) throw Unavailable();
        }
        catch (Exception e)
        {
            IsRegistered = false;
            RegisteredShortcut = null;
            RegistrationError = stop.IsCancellationRequested ? null : Unavailable(e);
            if (stop.IsCancellationRequested) started.TrySetCanceled(stop);
            else started.TrySetException(RegistrationError!);
            RegistrationChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            if (session is not null) portal?.CloseObject(session, "org.freedesktop.portal.Session");
            portal?.Dispose();
        }
    }

    private void UpdateShortcut(GioPortal.PortalVariant shortcuts)
    {
        IsRegistered = false;
        RegisteredShortcut = null;
        if (shortcuts.Type != "a(sa{sv})") throw Unavailable();
        for (var i = 0; (nuint)i < shortcuts.Count; i++)
        {
            using var item = shortcuts.Child(i);
            using var id = item.Child(0);
            if (id.String() != "capture") continue;
            using var properties = item.Child(1);
            using var trigger = properties.Lookup("trigger_description");
            RegisteredShortcut = trigger?.String();
            // Never claim the preferred key was bound when the desktop changed it.
            IsRegistered = true;
        }
        RegistrationError = IsRegistered ? null : Unavailable();
    }

    private static ProviderException Unavailable(Exception? inner = null) => new("hotkey_wayland",
        "系统未授权全局快捷键，或当前桌面不支持快捷键门户。截图按钮仍可使用；也可在系统键盘设置中为截图命令绑定自定义快捷键。", inner);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _stop?.Cancel();
        if (_worker is not null) await _worker;
        _worker = null;
        _stop?.Dispose();
        _stop = null;
        IsRegistered = false;
        RegisteredShortcut = null;
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PingYi.Core;

namespace PingYi.Infrastructure;

// Native Ubuntu GIO; no shell command, helper process, or new NuGet runtime.
// Each instance is confined to its dedicated worker and private GLib context.
internal sealed class GioPortal : IDisposable
{
    internal const string Destination = "org.freedesktop.portal.Desktop";
    internal const string DesktopPath = "/org/freedesktop/portal/desktop";
    private IntPtr _context, _connection;
    private bool _pushed;
    private Exception? _callbackError;
    private readonly Dictionary<uint, SignalCallback> _callbacks = [];

    internal GioPortal(CancellationToken token)
    {
        try
        {
            _context = g_main_context_new();
            g_main_context_push_thread_default(_context);
            _pushed = true;
            using var cancel = new NativeCancellation(token);
            var address = g_dbus_address_get_for_bus_sync(2, cancel.Pointer, out var error);
            Check(error, token);
            if (address == IntPtr.Zero) throw new PortalCallException("no_session_bus");
            try
            {
                _connection = g_dbus_connection_new_for_address_sync(
                    address, 1 | 8, IntPtr.Zero, cancel.Pointer, out error);
                Check(error, token);
                if (_connection == IntPtr.Zero) throw new PortalCallException("no_session_bus");
                g_dbus_connection_set_exit_on_close(_connection, false);
                Subscribe("org.freedesktop.DBus", "NameOwnerChanged", "/org/freedesktop/DBus", (_, value) =>
                {
                    if (value.Type != "(sss)") return;
                    using var name = value.Child(0);
                    using var previous = value.Child(1);
                    using var current = value.Child(2);
                    if (name.String() == Destination && previous.String().Length > 0 && current.String() != previous.String())
                        throw new PortalCallException("portal_restarted");
                }, "org.freedesktop.DBus", Destination);
            }
            finally { g_free(address); }
        }
        catch { Dispose(); throw; }
    }

    internal void RegisterApplication(CancellationToken token)
    {
        try
        {
            using var result = Call(DesktopPath, "org.freedesktop.host.portal.Registry", "Register",
                $"({Quote(AppEdition.IsComplete ? "pingyi-complete" : "pingyi")}, @a{{sv}} {{}})", token);
        }
        catch (PortalCallException e) when (e.Name is "org.freedesktop.DBus.Error.UnknownMethod" or
            "org.freedesktop.DBus.Error.UnknownInterface") { /* Older portals identify host apps themselves. */ }
    }

    internal PortalVariant Call(string path, string iface, string method, string parameters,
        CancellationToken token, int timeoutMilliseconds = 5000)
    {
        token.ThrowIfCancellationRequested();
        using var input = PortalVariant.Parse(parameters);
        using var cancel = new NativeCancellation(token);
        var reply = g_dbus_connection_call_sync(_connection, Destination, path, iface, method,
            input.DangerousGetHandle(), IntPtr.Zero, 0, timeoutMilliseconds, cancel.Pointer, out var error);
        if (error != IntPtr.Zero)
        {
            if (reply != IntPtr.Zero) g_variant_unref(reply);
            Check(error, token);
        }
        if (reply == IntPtr.Zero) throw new PortalCallException("empty_reply");
        return new PortalVariant(reply);
    }

    internal PortalResponse Request(string iface, string method, string requestToken,
        string parameters, CancellationToken token)
    {
        var unique = Marshal.PtrToStringUTF8(g_dbus_connection_get_unique_name(_connection))
            ?? throw new PortalCallException("missing_bus_name");
        string handle = $"{DesktopPath}/request/{unique.TrimStart(':').Replace('.', '_')}/{requestToken}";
        PortalResponse? response = null;
        // Subscribe before the method call; process callbacks only after the reply
        // updated handle, also supporting portals that return another request path.
        var subscription = Subscribe("org.freedesktop.portal.Request", "Response", null, (path, value) =>
        {
            if (path != handle || response is not null) return;
            if (value.Type != "(ua{sv})") throw new PortalCallException("invalid_response");
            using var code = value.Child(0);
            response = new PortalResponse(code.UInt32(), value.Child(1));
        });
        var completed = false;
        try
        {
            using var reply = Call(DesktopPath, iface, method, parameters, token);
            if (reply.Type != "(o)") throw new PortalCallException("invalid_handle");
            using var path = reply.Child(0);
            handle = path.String();
            if (!handle.StartsWith(DesktopPath + "/request/", StringComparison.Ordinal))
                throw new PortalCallException("invalid_handle");
            PumpUntil(() => response is not null, token);
            completed = true;
            return response!;
        }
        finally
        {
            Unsubscribe(subscription);
            if (!completed)
            {
                response?.Dispose();
                CloseObject(handle, "org.freedesktop.portal.Request");
            }
        }
    }

    internal uint Subscribe(string iface, string member, string? path, Action<string, PortalVariant> receive,
        string sender = Destination, string? arg0 = null)
    {
        SignalCallback callback = (_, _, objectPath, _, _, parameters, _) =>
        {
            // Never let a managed exception unwind through a native signal callback.
            try
            {
                using var value = new PortalVariant(g_variant_ref(parameters));
                receive(Marshal.PtrToStringUTF8(objectPath) ?? string.Empty, value);
            }
            catch (Exception e) { _callbackError = e; }
        };
        var id = g_dbus_connection_signal_subscribe(_connection, sender, iface, member,
            path, arg0, 0, callback, IntPtr.Zero, IntPtr.Zero);
        _callbacks.Add(id, callback);
        return id;
    }

    internal void Unsubscribe(uint id)
    {
        if (_callbacks.Remove(id, out var callback))
        {
            // Same thread as subscription: callbacks cannot occur after this returns.
            g_dbus_connection_signal_unsubscribe(_connection, id);
            GC.KeepAlive(callback);
        }
    }

    internal void PumpUntil(Func<bool> done, CancellationToken token)
    {
        using var wake = token.Register(() => g_main_context_wakeup(_context));
        while (!done())
        {
            token.ThrowIfCancellationRequested();
            if (_callbackError is { } error) throw error;
            if (g_dbus_connection_is_closed(_connection)) throw new PortalCallException("bus_closed");
            // Native blocking dispatch; no persistent timer or busy polling loop.
            g_main_context_iteration(_context, true);
        }
        token.ThrowIfCancellationRequested();
        if (_callbackError is { } lastError) throw lastError;
    }

    internal void CloseObject(string path, string iface)
    {
        try { using var _ = Call(path, iface, "Close", "()", CancellationToken.None, 1000); }
        catch (Exception e) when (e is PortalCallException or OperationCanceledException) { }
    }

    public void Dispose()
    {
        if (_connection != IntPtr.Zero)
        {
            foreach (var id in _callbacks.Keys.ToArray()) Unsubscribe(id);
            // Release only our private connection, not the process-wide session bus.
            g_object_unref(_connection);
            _connection = IntPtr.Zero;
        }
        if (_pushed) { g_main_context_pop_thread_default(_context); _pushed = false; }
        if (_context != IntPtr.Zero) { g_main_context_unref(_context); _context = IntPtr.Zero; }
    }

    internal static string Quote(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'")
        .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "'";

    private static void Check(IntPtr error, CancellationToken token)
    {
        if (error == IntPtr.Zero) return;
        var remote = g_dbus_error_get_remote_error(error);
        string name;
        try { name = Marshal.PtrToStringUTF8(remote) ?? "transport_error"; }
        finally { if (remote != IntPtr.Zero) g_free(remote); g_error_free(error); }
        token.ThrowIfCancellationRequested();
        // Raw D-Bus messages may contain file paths. Surface only a stable category.
        throw new PortalCallException(name);
    }

    private sealed class NativeCancellation : IDisposable
    {
        internal IntPtr Pointer { get; } = g_cancellable_new();
        private readonly CancellationTokenRegistration _registration;
        internal NativeCancellation(CancellationToken token) => _registration = token.Register(() => g_cancellable_cancel(Pointer));
        public void Dispose() { _registration.Dispose(); g_object_unref(Pointer); }
    }

    internal sealed class PortalCallException(string name) : Exception("Desktop portal request failed.")
    { internal string Name { get; } = name; }
    internal sealed record PortalResponse(uint Code, PortalVariant Values) : IDisposable
    { public void Dispose() => Values.Dispose(); }

    internal sealed class PortalVariant : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal PortalVariant(IntPtr handle) : base(true) => SetHandle(handle);
        internal string Type => Marshal.PtrToStringUTF8(g_variant_get_type_string(handle)) ?? string.Empty;
        internal nuint Count => g_variant_n_children(handle);
        internal PortalVariant Child(int index) => index >= 0 && (nuint)index < Count
            ? new(g_variant_get_child_value(handle, (nuint)index)) : throw new PortalCallException("invalid_response");
        internal PortalVariant? Lookup(string key)
        {
            if (Type != "a{sv}") throw new PortalCallException("invalid_response");
            var value = g_variant_lookup_value(handle, key, IntPtr.Zero);
            return value == IntPtr.Zero ? null : new(value);
        }
        internal string String() => Type is "s" or "o"
            ? Marshal.PtrToStringUTF8(g_variant_get_string(handle, out _)) ?? string.Empty
            : throw new PortalCallException("invalid_response");
        internal uint UInt32() => Type == "u" ? g_variant_get_uint32(handle) : throw new PortalCallException("invalid_response");
        internal static PortalVariant Parse(string text)
        {
            var value = g_variant_parse(IntPtr.Zero, text, IntPtr.Zero, IntPtr.Zero, out var error);
            if (error != IntPtr.Zero) { g_error_free(error); throw new PortalCallException("invalid_request"); }
            if (value == IntPtr.Zero) throw new PortalCallException("invalid_request");
            // g_variant_parse returns a non-floating owned reference.
            return new PortalVariant(value);
        }
        protected override bool ReleaseHandle() { g_variant_unref(handle); return true; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SignalCallback(IntPtr connection, IntPtr sender,
        IntPtr objectPath, IntPtr iface, IntPtr signal, IntPtr parameters, IntPtr userData);
    private const string Gio = "libgio-2.0.so.0", Glib = "libglib-2.0.so.0", GObject = "libgobject-2.0.so.0";
    [DllImport(Glib)] private static extern IntPtr g_main_context_new();
    [DllImport(Glib)] private static extern void g_main_context_push_thread_default(IntPtr context);
    [DllImport(Glib)] private static extern void g_main_context_pop_thread_default(IntPtr context);
    [DllImport(Glib)] private static extern void g_main_context_unref(IntPtr context);
    [DllImport(Glib)] private static extern void g_main_context_wakeup(IntPtr context);
    [DllImport(Glib)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool g_main_context_iteration(IntPtr context, [MarshalAs(UnmanagedType.Bool)] bool mayBlock);
    [DllImport(Gio)] private static extern IntPtr g_cancellable_new();
    [DllImport(Gio)] private static extern void g_cancellable_cancel(IntPtr cancel);
    [DllImport(Gio)] private static extern IntPtr g_dbus_address_get_for_bus_sync(int bus, IntPtr cancel, out IntPtr error);
    [DllImport(Gio)] private static extern IntPtr g_dbus_connection_new_for_address_sync(IntPtr address, int flags, IntPtr observer, IntPtr cancel, out IntPtr error);
    [DllImport(Gio)] private static extern void g_dbus_connection_set_exit_on_close(IntPtr connection, [MarshalAs(UnmanagedType.Bool)] bool exit);
    [DllImport(Gio)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool g_dbus_connection_is_closed(IntPtr connection);
    [DllImport(Gio)] private static extern IntPtr g_dbus_connection_get_unique_name(IntPtr connection);
    [DllImport(Gio)] private static extern IntPtr g_dbus_connection_call_sync(IntPtr connection, string destination, string path,
        string iface, string method, IntPtr parameters, IntPtr replyType, int flags, int timeout, IntPtr cancel, out IntPtr error);
    [DllImport(Gio)] private static extern uint g_dbus_connection_signal_subscribe(IntPtr connection, string sender, string iface,
        string member, string? path, string? arg0, int flags, SignalCallback callback, IntPtr userData, IntPtr destroy);
    [DllImport(Gio)] private static extern void g_dbus_connection_signal_unsubscribe(IntPtr connection, uint subscription);
    [DllImport(Gio)] private static extern IntPtr g_dbus_error_get_remote_error(IntPtr error);
    [DllImport(GObject)] private static extern void g_object_unref(IntPtr obj);
    [DllImport(Glib)] private static extern void g_free(IntPtr memory);
    [DllImport(Glib)] private static extern void g_error_free(IntPtr error);
    [DllImport(Glib)] private static extern IntPtr g_variant_parse(IntPtr type, string text, IntPtr limit, IntPtr end, out IntPtr error);
    [DllImport(Glib)] private static extern IntPtr g_variant_ref(IntPtr variant);
    [DllImport(Glib)] private static extern void g_variant_unref(IntPtr variant);
    [DllImport(Glib)] private static extern IntPtr g_variant_get_type_string(IntPtr variant);
    [DllImport(Glib)] private static extern nuint g_variant_n_children(IntPtr variant);
    [DllImport(Glib)] private static extern IntPtr g_variant_get_child_value(IntPtr variant, nuint index);
    [DllImport(Glib)] private static extern IntPtr g_variant_lookup_value(IntPtr variant, string key, IntPtr expectedType);
    [DllImport(Glib)] private static extern IntPtr g_variant_get_string(IntPtr variant, out nuint length);
    [DllImport(Glib)] private static extern uint g_variant_get_uint32(IntPtr variant);
}

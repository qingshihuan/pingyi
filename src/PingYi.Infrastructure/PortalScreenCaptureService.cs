using System.Diagnostics;
using System.Runtime.InteropServices;
using PingYi.Core;
using SkiaSharp;

namespace PingYi.Infrastructure;

/// <summary>Wayland capture through the public XDG Screenshot portal, never XWayland root pixels.</summary>
public sealed class PortalScreenCaptureService : IInteractiveScreenCaptureService
{
    public Task<ImageFrame?> CaptureSelectionAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    public async Task<ImageFrame> CaptureDesktopAsync(CancellationToken cancellationToken = default) =>
        await CaptureSelectionAsync(cancellationToken).ConfigureAwait(false)
        ?? throw new OperationCanceledException("System screenshot canceled.", cancellationToken);

    private static ImageFrame? Capture(CancellationToken cancellationToken)
    {
        try
        {
            var uri = GioScreenshotRequest.Run(cancellationToken);
            return uri is null ? null : ReadPortalImage(uri, cancellationToken);
        }
        catch (DllNotFoundException) { throw Unavailable(); }
        catch (EntryPointNotFoundException) { throw Unavailable(); }
    }
    internal static ProviderException Unavailable() => new("linux_portal_unavailable", "系统截图门户不可用，请安装并启用桌面截图门户后重试。");

    internal static ImageFrame ReadPortalImage(string value, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsFile ||
            (!string.IsNullOrEmpty(uri.Host) && uri.Host != "localhost"))
            throw InvalidImage();
        var path = uri.LocalPath;
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).LinkTarget is not null) throw InvalidImage();
        // Only clean up portal-created temporary copies. Never delete arbitrary Pictures/Documents files.
        var temporary = IsUnder(path, Path.GetTempPath()) || IsUnder(path, Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is <= 0 or > 100 * 1024 * 1024) throw InvalidImage();
            var bytes = new byte[checked((int)input.Length)];
            input.ReadExactly(bytes);
            cancellationToken.ThrowIfCancellationRequested();
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
                (long)codec.Info.Width * codec.Info.Height > 80_000_000) throw InvalidImage();
            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap is null) throw InvalidImage();
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            cancellationToken.ThrowIfCancellationRequested();
            // Portals return image pixels, not the selected region's global desktop coordinates.
            return new ImageFrame(png.ToArray(), bitmap.Width, bitmap.Height,
                new PixelRect(0, 0, bitmap.Width, bitmap.Height));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw InvalidImage(); }
        finally
        {
            if (temporary)
                try { File.Delete(path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
    internal static void CleanupCanceledPortalCopy(string? value)
    {
        try
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile &&
                (string.IsNullOrEmpty(uri.Host) || uri.Host == "localhost") &&
                (IsUnder(uri.LocalPath, Path.GetTempPath()) || IsUnder(uri.LocalPath, Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"))) &&
                new FileInfo(uri.LocalPath).LinkTarget is null)
                File.Delete(uri.LocalPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
    }
    private static bool IsUnder(string path, string? directory) => !string.IsNullOrWhiteSpace(directory) &&
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);
    private static ProviderException InvalidImage() => new("linux_capture_invalid", "系统返回的截图无效或过大。");
}

// GIO is provided by the Linux desktop; no Python helper, shell invocation or private GNOME API.
// Subscribe before calling Screenshot on the SAME bus connection (the returned path contains its unique name).
internal static class GioScreenshotRequest
{
    private const string Service = "org.freedesktop.portal.Desktop";
    private const string DesktopPath = "/org/freedesktop/portal/desktop";
    private const string RequestInterface = "org.freedesktop.portal.Request";
    public static string? Run(CancellationToken userToken)
    {
        userToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(userToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var token = timeout.Token;
        var context = g_main_context_new();
        g_main_context_push_thread_default(context);
        var cancellable = g_cancellable_new();
        IntPtr connection = IntPtr.Zero;
        uint subscription = 0;
        string? requestPath = null;
        bool finished = false, destroyed = false;
        uint response = 2;
        string? imageUri = null;
        SignalCallback callback = (_, _, _, _, _, parameters, _) =>
        {
            try
            {
                if (!IsType(parameters, "(ua{sv})")) { finished = true; return; }
                var code = g_variant_get_child_value(parameters, 0);
                var results = g_variant_get_child_value(parameters, 1);
                try
                {
                    response = g_variant_get_uint32(code);
                    var uriType = g_variant_type_new("s");
                    var value = g_variant_lookup_value(results, "uri", uriType);
                    g_variant_type_free(uriType);
                    if (value != IntPtr.Zero)
                    {
                        try { imageUri = Marshal.PtrToStringUTF8(g_variant_get_string(value, IntPtr.Zero)); }
                        finally { g_variant_unref(value); }
                    }
                }
                finally { g_variant_unref(code); g_variant_unref(results); }
            }
            catch { response = 2; } // Never unwind managed exceptions into GLib.
            finally { finished = true; }
        };
        DestroyCallback onDestroyed = _ => destroyed = true;
        using var registration = token.Register(() => g_cancellable_cancel(cancellable));
        try
        {
            connection = g_bus_get_sync(2, cancellable, out var error);
            if (error != IntPtr.Zero) { g_error_free(error); token.ThrowIfCancellationRequested(); throw PortalScreenCaptureService.Unavailable(); }
            if (connection == IntPtr.Zero) throw PortalScreenCaptureService.Unavailable();
            g_dbus_connection_set_exit_on_close(connection, false);
            var sender = Marshal.PtrToStringUTF8(g_dbus_connection_get_unique_name(connection));
            if (string.IsNullOrEmpty(sender)) throw PortalScreenCaptureService.Unavailable();
            var handleToken = "pingyi_" + Guid.NewGuid().ToString("N");
            requestPath = DesktopPath + "/request/" + sender.TrimStart(':').Replace('.', '_') + "/" + handleToken;
            subscription = g_dbus_connection_signal_subscribe(connection, Service, RequestInterface, "Response",
                requestPath, IntPtr.Zero, 0, callback, IntPtr.Zero, onDestroyed);
            var parameters = ScreenshotParameters(handleToken);
            var replyType = g_variant_type_new("(o)");
            IntPtr reply;
            try
            {
                reply = g_dbus_connection_call_sync(connection, Service, DesktopPath,
                    "org.freedesktop.portal.Screenshot", "Screenshot", parameters, replyType, 0, 10000, cancellable, out error);
            }
            finally { g_variant_type_free(replyType); g_variant_unref(parameters); }
            if (error != IntPtr.Zero) { g_error_free(error); token.ThrowIfCancellationRequested(); throw PortalScreenCaptureService.Unavailable(); }
            if (reply == IntPtr.Zero) throw PortalScreenCaptureService.Unavailable();
            try
            {
                var child = g_variant_get_child_value(reply, 0);
                try
                {
                    var returnedPath = Marshal.PtrToStringUTF8(g_variant_get_string(child, IntPtr.Zero));
                    if (returnedPath != requestPath) throw PortalScreenCaptureService.Unavailable();
                }
                finally { g_variant_unref(child); }
            }
            finally { g_variant_unref(reply); }
            while (!finished)
            {
                token.ThrowIfCancellationRequested();
                if (g_dbus_connection_is_closed(connection)) throw PortalScreenCaptureService.Unavailable();
                while (g_main_context_iteration(context, false)) { token.ThrowIfCancellationRequested(); }
                if (!finished) Thread.Sleep(15);
            }
            token.ThrowIfCancellationRequested();
            if (response == 1) return null; // User canceled the compositor dialog; not an error.
            if (response != 0 || string.IsNullOrEmpty(imageUri))
                throw new ProviderException("linux_portal_failed", "系统未能提供截图，请重试并完成系统截图界面。");
            return imageUri;
        }
        catch (OperationCanceledException) when (!userToken.IsCancellationRequested)
        { throw new ProviderException("linux_portal_timeout", "等待系统截图界面超时。"); }
        finally
        {
            // Close only our own request; canceling an application operation must dismiss the portal UI.
            if (!finished && connection != IntPtr.Zero && requestPath is not null)
            {
                var reply = g_dbus_connection_call_sync(connection, Service, requestPath, RequestInterface, "Close",
                    IntPtr.Zero, IntPtr.Zero, 0, 1000, IntPtr.Zero, out var error);
                if (reply != IntPtr.Zero) g_variant_unref(reply);
                if (error != IntPtr.Zero) g_error_free(error);
            }
            if (subscription != 0)
            {
                g_dbus_connection_signal_unsubscribe(connection, subscription);
                // Dispose subscription on the thread/context that owns it, before releasing delegates.
                while (!destroyed) { if (!g_main_context_iteration(context, false)) Thread.Sleep(1); }
            }
            if (token.IsCancellationRequested && response == 0)
                PortalScreenCaptureService.CleanupCanceledPortalCopy(imageUri);
            registration.Dispose();
            if (connection != IntPtr.Zero) g_object_unref(connection);
            g_object_unref(cancellable);
            g_main_context_pop_thread_default(context);
            g_main_context_unref(context);
            GC.KeepAlive(callback); GC.KeepAlive(onDestroyed);
        }
    }
    private static IntPtr ScreenshotParameters(string token)
    {
        var type = g_variant_type_new("a{sv}");
        var builder = g_variant_builder_new(type);
        g_variant_type_free(type);
        try
        {
            Add("handle_token", g_variant_new_string(token));
            Add("interactive", g_variant_new_boolean(true));
            Add("modal", g_variant_new_boolean(false));
            var options = g_variant_builder_end(builder);
            return g_variant_ref_sink(g_variant_new_tuple([g_variant_new_string(""), options], 2));
        }
        finally { g_variant_builder_unref(builder); }
        void Add(string key, IntPtr value) => g_variant_builder_add_value(builder,
            g_variant_new_dict_entry(g_variant_new_string(key), g_variant_new_variant(value)));
    }
    private static bool IsType(IntPtr value, string signature)
    {
        var type = g_variant_type_new(signature);
        try { return g_variant_is_of_type(value, type); }
        finally { g_variant_type_free(type); }
    }
    private const string Gio = "libgio-2.0.so.0", Glib = "libglib-2.0.so.0", Object = "libgobject-2.0.so.0";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SignalCallback(IntPtr c, IntPtr s, IntPtr p, IntPtr i, IntPtr m, IntPtr v, IntPtr d);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyCallback(IntPtr data);
    [DllImport(Gio)] private static extern IntPtr g_bus_get_sync(int type, IntPtr cancel, out IntPtr error);
    [DllImport(Gio)] private static extern IntPtr g_cancellable_new();
    [DllImport(Gio)] private static extern void g_cancellable_cancel(IntPtr cancellable);
    [DllImport(Gio)] private static extern IntPtr g_dbus_connection_get_unique_name(IntPtr c);
    [DllImport(Gio)] private static extern void g_dbus_connection_set_exit_on_close(IntPtr c, [MarshalAs(UnmanagedType.Bool)] bool exit);
    [DllImport(Gio)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool g_dbus_connection_is_closed(IntPtr c);
    [DllImport(Gio)] private static extern uint g_dbus_connection_signal_subscribe(IntPtr c, string sender, string iface, string member,
        string path, IntPtr arg0, int flags, SignalCallback callback, IntPtr data, DestroyCallback free);
    [DllImport(Gio)] private static extern void g_dbus_connection_signal_unsubscribe(IntPtr c, uint id);
    [DllImport(Gio)] private static extern IntPtr g_dbus_connection_call_sync(IntPtr c, string service, string path, string iface,
        string method, IntPtr parameters, IntPtr replyType, int flags, int timeout, IntPtr cancel, out IntPtr error);
    [DllImport(Glib)] private static extern IntPtr g_main_context_new();
    [DllImport(Glib)] private static extern void g_main_context_push_thread_default(IntPtr c);
    [DllImport(Glib)] private static extern void g_main_context_pop_thread_default(IntPtr c);
    [DllImport(Glib)] private static extern void g_main_context_unref(IntPtr c);
    [DllImport(Glib)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool g_main_context_iteration(IntPtr c, [MarshalAs(UnmanagedType.Bool)] bool block);
    [DllImport(Object)] private static extern void g_object_unref(IntPtr value);
    [DllImport(Glib)] private static extern void g_error_free(IntPtr error);
    [DllImport(Glib)] private static extern IntPtr g_variant_type_new(string type);
    [DllImport(Glib)] private static extern void g_variant_type_free(IntPtr type);
    [DllImport(Glib)] private static extern IntPtr g_variant_builder_new(IntPtr type);
    [DllImport(Glib)] private static extern void g_variant_builder_add_value(IntPtr builder, IntPtr value);
    [DllImport(Glib)] private static extern IntPtr g_variant_builder_end(IntPtr builder);
    [DllImport(Glib)] private static extern void g_variant_builder_unref(IntPtr builder);
    [DllImport(Glib)] private static extern IntPtr g_variant_new_string([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Glib)] private static extern IntPtr g_variant_new_boolean([MarshalAs(UnmanagedType.Bool)] bool value);
    [DllImport(Glib)] private static extern IntPtr g_variant_new_variant(IntPtr value);
    [DllImport(Glib)] private static extern IntPtr g_variant_new_dict_entry(IntPtr key, IntPtr value);
    [DllImport(Glib)] private static extern IntPtr g_variant_new_tuple(IntPtr[] children, nuint count);
    [DllImport(Glib)] private static extern IntPtr g_variant_ref_sink(IntPtr value);
    [DllImport(Glib)] private static extern void g_variant_unref(IntPtr value);
    [DllImport(Glib)] private static extern IntPtr g_variant_get_child_value(IntPtr value, nuint index);
    [DllImport(Glib)] private static extern uint g_variant_get_uint32(IntPtr value);
    [DllImport(Glib)] private static extern IntPtr g_variant_lookup_value(IntPtr dict, string key, IntPtr type);
    [DllImport(Glib)] private static extern IntPtr g_variant_get_string(IntPtr value, IntPtr length);
    [DllImport(Glib)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool g_variant_is_of_type(IntPtr value, IntPtr type);
}

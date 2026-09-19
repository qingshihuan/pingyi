namespace PingYi.Infrastructure;

public enum LinuxDesktopSession { X11, Wayland, Unavailable }

public static class LinuxDesktop
{
    public static LinuxDesktopSession Session => Detect(
        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
        Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
        Environment.GetEnvironmentVariable("DISPLAY"));

    // DISPLAY is also present under XWayland. Never treat it alone as permission
    // to capture the Wayland desktop or grab the compositor's global keys.
    public static LinuxDesktopSession Detect(string? session, string? wayland, string? display)
    {
        if (string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase)) return LinuxDesktopSession.Wayland;
        if (string.Equals(session, "x11", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(display) ? LinuxDesktopSession.Unavailable : LinuxDesktopSession.X11;
        if (!string.IsNullOrWhiteSpace(wayland)) return LinuxDesktopSession.Wayland;
        return string.IsNullOrWhiteSpace(display) ? LinuxDesktopSession.Unavailable : LinuxDesktopSession.X11;
    }
}

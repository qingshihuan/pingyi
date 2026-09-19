using PingYi.Core;

namespace PingYi.Infrastructure;

public static class LinuxDesktopSession
{
    public static bool IsWayland => OperatingSystem.IsLinux() && IsWaylandSession(
        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
        Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    public static bool IsWaylandSession(string? type, string? waylandDisplay) =>
        string.Equals(type, "wayland", StringComparison.OrdinalIgnoreCase) ||
        (!string.Equals(type, "x11", StringComparison.OrdinalIgnoreCase) &&
         !string.IsNullOrWhiteSpace(waylandDisplay));
}

/// <summary>Wayland does not permit XGrabKey. The desktop owns the binding to --capture.</summary>
public sealed class DesktopManagedHotkeyService : IGlobalHotkeyService
{
    public event EventHandler? Pressed { add { } remove { } }
    public Task StartAsync(string shortcut, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = GlobalHotkeyGesture.Parse(shortcut);
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static class X11Threading
{
    private static readonly Lazy<int> Initialized = new(XInitThreads);
    public static void EnsureInitialized() => _ = Initialized.Value;
    [System.Runtime.InteropServices.DllImport("libX11.so.6")]
    private static extern int XInitThreads();
}

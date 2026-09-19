using PingYi.Core;

namespace PingYi.App;

internal static class HotkeyFeedback
{
    internal static string Badge(IGlobalHotkeyService? service) => service is IGlobalHotkeyStatus status
        ? status.IsRegistered ? status.RegisteredShortcut ?? UiText.Get("Linux.HotkeySystem") : UiText.Get("Linux.ClickCapture")
        : AppSettings.DefaultHotkey;
    internal static string Message(IGlobalHotkeyService? service)
    {
        if (service is not IGlobalHotkeyStatus status) return UiText.Get("Linux.HotkeyWaiting");
        if (status.IsRegistered) return UiText.Get("Linux.HotkeyOn") + Badge(service);
        if (status.RegistrationError is { } error) return UiText.Error(error);
        return UiText.Get("Linux.HotkeyOff");
    }
    internal static string? ErrorKey(string code) => code switch
    {
        "hotkey_conflict" => "Linux.HotkeyConflict",
        "hotkey_wayland" => "Linux.HotkeyWayland",
        "hotkey_display_unavailable" or "hotkey_key_unmapped" or "hotkey_registration_failed" => "Linux.HotkeyFailed",
        "capture_portal_unavailable" or "capture_wayland_portal_required" => "Linux.PortalUnavailable",
        "capture_portal_timeout" => "Linux.PortalTimeout",
        "capture_portal_denied" => "Linux.PortalDenied",
        "capture_portal_image" => "Linux.PortalImage",
        _ => null
    };
}

using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

internal static class LinuxDesktopUi
{
    public static string ExternalShortcutHelp => UiText.IsEnglish
        ? "Wayland: bind the capture command in Ubuntu Settings → Keyboard → Custom Shortcuts. The capture button works independently."
        : "Wayland：请在 Ubuntu 设置 → 键盘 → 自定义快捷键中绑定截图命令；也可直接点击截图按钮。";
    public static string ShortcutLabel(string gesture) => LinuxDesktopSession.IsWayland
        ? (UiText.IsEnglish ? "System shortcut" : "系统快捷键")
        : gesture.Replace("+", "  ", StringComparison.Ordinal);
    public static string PortalPrivacyNote => !LinuxDesktopSession.IsWayland ? "" : UiText.IsEnglish
        ? "\nWayland screenshots use the desktop's permission dialog. The portal may create a temporary image; PingYi reads it locally and removes temporary-directory copies. Files saved elsewhere by the desktop remain under its control."
        : "\nWayland 截图通过系统授权界面取得。门户可能创建临时图片；屏译在本地读取，并清理临时目录中的副本。系统保存到其他目录的截图仍由系统管理。";
    public static string CaptureCommand
    {
        get
        {
            var process = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PingYi.App");
            var assembly = Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? " " + Quote(typeof(App).Assembly.Location) : "";
            return Quote(process) + assembly + " --capture";
        }
    }
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`") + "\"";
    public static string DescribeError(Exception exception)
    {
        if (exception is ProviderException error)
            return error.Code switch
            {
                "hotkey_conflict" => UiText.IsEnglish
                    ? "The shortcut is already in use. Choose another in Settings. The capture button remains available."
                    : "快捷键已被系统或其他程序占用，请在设置中更换；仍可使用截图按钮。",
                "linux_portal_unavailable" => UiText.IsEnglish
                    ? "The desktop screenshot portal is unavailable. Install/enable xdg-desktop-portal and the desktop backend (Ubuntu: xdg-desktop-portal-gnome), then sign in again."
                    : "系统截图门户不可用。请安装或启用 xdg-desktop-portal 及桌面后端（Ubuntu：xdg-desktop-portal-gnome），然后重新登录。",
                "linux_portal_failed" => UiText.IsEnglish
                    ? "The desktop could not provide a screenshot. Retry and complete the system screenshot/permission dialog."
                    : "系统未能提供截图，请重试并完成系统截图或授权界面。",
                "linux_portal_timeout" => UiText.IsEnglish
                    ? "The system screenshot dialog timed out. Start a new capture and complete the system dialog."
                    : "等待系统截图界面超时，请重新截图并完成系统界面操作。",
                "linux_capture_invalid" => UiText.IsEnglish
                    ? "The desktop returned an invalid or oversized screenshot. Select a smaller area and retry."
                    : "系统返回的截图无效或过大，请缩小范围后重试。",
                _ => UiText.Error(exception)
            };
        return UiText.Error(exception);
    }
}

public partial class SettingsWindow
{
    private void InitializeLinuxHelp()
    {
        RefreshLinuxHelp();
        UiText.LanguageChanged += LinuxHelpLanguageChanged;
        Closed += (_, _) => UiText.LanguageChanged -= LinuxHelpLanguageChanged;
    }
    private void LinuxHelpLanguageChanged(object? sender, EventArgs e) => RefreshLinuxHelp();
    private void RefreshLinuxHelp()
    {
        LinuxShortcutHelp.IsVisible = OperatingSystem.IsLinux();
        HotkeyBox.PlaceholderText = AppSettings.DefaultHotkey;
        HotkeyBox.IsReadOnly = LinuxDesktopSession.IsWayland; // The compositor owns bindings on Wayland.
        LinuxShortcutHintText.Text = LinuxDesktopSession.IsWayland ? LinuxDesktopUi.ExternalShortcutHelp : UiText.IsEnglish
            ? "Linux default: Ctrl+Alt+Shift+D. Conflicts do not disable the capture button. You may also bind this command in the desktop keyboard settings."
            : "Linux 默认使用 Ctrl+Alt+Shift+D；快捷键冲突不影响按钮截图。也可在系统键盘设置中绑定以下命令。";
        LinuxCaptureCommandBox.Text = LinuxDesktopUi.CaptureCommand;
        CopyLinuxCaptureCommandButton.Content = UiText.IsEnglish ? "Copy capture command" : "复制截图命令";
    }
    private async void CopyLinuxCaptureCommand_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard is { } clipboard) await clipboard.SetTextAsync(LinuxDesktopUi.CaptureCommand);
        }
        catch (Exception exception) { SetGlobalStatus(UiText.Error(exception), true); }
    }
}

using Avalonia.Threading;
using PingYi.Core;

namespace PingYi.App;

public partial class App
{
    private void OnHotkeyChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_isExiting) return;
        if (_mainWindow is MainWindow window) window.RefreshHotkeyPresentation();
        // The menu is unchanged. Keep its native tray instance and D-Bus watcher alive;
        // disposing it to refresh a tooltip can raise an asynchronous cancellation error
        // when no StatusNotifier host is present on the Linux desktop.
        if (_trayIcon is { } tray)
            tray.ToolTipText = $"{(UiText.IsEnglish ? "PingYi" : AppEdition.ProductName)} · "
                + LinuxDesktopUi.ShortcutLabel(_services?.Settings.Hotkey ?? AppSettings.DefaultHotkey);
    });
}

public partial class MainWindow
{
    internal void RefreshHotkeyPresentation()
    {
        if (_services is null) return;
        CaptureHotkeyText.Text = LinuxDesktopUi.ShortcutLabel(_services.Settings.Hotkey);
        _ = RefreshDashboardAsync();
    }
}

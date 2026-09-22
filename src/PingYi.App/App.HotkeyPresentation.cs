using Avalonia.Threading;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class App
{
    private void OnHotkeyChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_isExiting) return;
        if (_mainWindow is MainWindow window) window.RefreshHotkeyPresentation();
        RefreshTrayLanguage(sender, e);
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

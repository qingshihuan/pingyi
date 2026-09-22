using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class App
{
    internal void RefreshHotkeyPresentation()
    {
        if (_isExiting || _services is null) return;
        if (_mainWindow is MainWindow main) main.RefreshHotkeyPresentation();
        if (_trayIcon is not null)
            _trayIcon.ToolTipText = $"{(UiText.IsEnglish ? "PingYi" : AppEdition.ProductName)} · {LinuxDesktopUi.ShortcutLabel(_services.Settings.Hotkey)}";
    }
}

public partial class MainWindow
{
    internal void RefreshHotkeyPresentation() => LoadSettings();
}

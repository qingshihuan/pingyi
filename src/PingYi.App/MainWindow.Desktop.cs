using Avalonia.Threading;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class MainWindow
{
    private void AttachHotkeyFeedback()
    {
        if (_services?.HotkeyService is IGlobalHotkeyStatus status)
        {
            status.RegistrationChanged += HotkeyStateChanged;
            Closed += (_, _) => status.RegistrationChanged -= HotkeyStateChanged;
        }
        RefreshDesktopFeedback();
    }
    private void HotkeyStateChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshDesktopFeedback);
    private void RefreshDesktopFeedback()
    {
        CaptureHotkeyText.Text = HotkeyFeedback.Badge(_services?.HotkeyService);
        HotkeyRegistrationText.Text = HotkeyFeedback.Message(_services?.HotkeyService);
        DesktopCaptureHintText.IsVisible = OperatingSystem.IsLinux();
        DesktopCaptureHintText.Text = UiText.Get(LinuxDesktop.Session == LinuxDesktopSession.Wayland ? "Linux.WaylandCapture" : "Linux.X11Capture");
    }
}

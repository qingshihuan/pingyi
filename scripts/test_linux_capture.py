"""Routing and package contracts for Linux capture, without reading a desktop."""
from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[1]
class LinuxContracts(unittest.TestCase):
    def test_first_launch_capture_is_not_only_forwarded(self):
        app = (ROOT/'src/PingYi.App/App.axaml.cs').read_text(encoding='utf8')
        self.assertIn('CommandFromArguments(desktop.Args', app)
        self.assertIn('else if (launchCommand == "capture")', app)
        self.assertIn('await _captureCoordinator.StartCaptureAsync(_mainShell)', app)
    def test_packages_supply_shortcut_action_and_portal_dependency(self):
        desktop=(ROOT/'packaging/linux/pingyi.desktop').read_text()
        self.assertIn('[Desktop Action Capture]',desktop)
        self.assertIn('Exec=pingyi --capture',desktop)
        control=(ROOT/'packaging/linux/control').read_text()
        self.assertIn('libglib2.0-0 | libglib2.0-0t64',control)
        self.assertIn('xdg-desktop-portal-gnome',control)
        self.assertIn(r's/^Exec=pingyi\(.*\)$/Exec=pingyi-complete\1/', (ROOT/'scripts/publish.sh').read_text())
    def test_portal_lifetime_and_interactive_selection_are_explicit(self):
        source=(ROOT/'src/PingYi.Infrastructure/GioPortal.cs').read_text()
        self.assertLess(source.index('var subscription = Subscribe'),source.index('using var reply = Call(DesktopPath, iface, method'))
        self.assertIn('NameOwnerChanged',source)
        self.assertIn('CloseObject(handle, "org.freedesktop.portal.Request")',source)
        selector=(ROOT/'src/PingYi.App/CaptureSelectionWorkflow.cs').read_text()
        self.assertLess(selector.index('interactive.CaptureSelectionAsync'),selector.index('CaptureDesktopAsync'))
        home=(ROOT/'src/PingYi.App/MainWindow.axaml').read_text()
        self.assertIn('HotkeyRegistrationText',home)
if __name__ == '__main__': unittest.main()

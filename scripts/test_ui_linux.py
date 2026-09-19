"""Regression contracts for platform-specific capture and independent hotkeys."""
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
class LinuxUiContracts(unittest.TestCase):
    def test_primary_process_honors_capture_flag(self):
        source = (ROOT/'src/PingYi.App/App.axaml.cs').read_text(encoding='utf-8-sig')
        self.assertIn('CommandFromArguments(desktop.Args ?? []) == "capture"', source)
        self.assertIn('DesktopManagedHotkeyService', source)
    def test_selection_wait_is_not_pixel_read_timeout(self):
        source = (ROOT/'src/PingYi.App/CaptureSelectionRouter.cs').read_text()
        self.assertLess(source.index('IInteractiveScreenCaptureService'), source.index('CancelAfter'))
    def test_wayland_does_not_attempt_to_capture_xwayland_root(self):
        source = (ROOT/'src/PingYi.Infrastructure/ScreenCaptureServices.cs').read_text()
        self.assertIn('LinuxDesktopSession.IsWayland', source)
        self.assertIn('new PortalScreenCaptureService()', source)
    def test_capture_errors_restore_a_visible_window(self):
        source = (ROOT/'src/PingYi.App/CaptureCoordinator.cs').read_text()
        self.assertIn('mainWindow?.Show();', source)
        self.assertIn('LinuxDesktopUi.DescribeError(exception)', source)

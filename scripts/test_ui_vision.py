"""Vision integration contracts supplement the executable C# regressions."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'src/PingYi.App'


class VisionContracts(unittest.TestCase):
    def test_no_ocr_precondition_or_silent_fallback_for_image_analysis(self):
        code = (APP / 'CaptureCoordinator.cs').read_text(encoding='utf8')
        # Text and automatic probing now live in separate partial files. Manual visual
        # tasks must dispatch directly, rather than relying on old local-variable names.
        dispatch = code[code.index('private async Task ProcessAsync('):code.index('private static async Task<T> WithTimeoutAsync')]
        self.assertRegex(dispatch, r'case CapturePurpose\.DescribeImage:\s*case CapturePurpose\.ReconstructPrompt:\s*await ProcessImageAnalysisAsync\(operation, window, image\); break;')
        self.assertIn('case CapturePurpose.TranslateText: await ProcessTextAsync', dispatch)
        self.assertIn('case CapturePurpose.Auto: await ProcessAutomaticAsync', dispatch)
        self.assertNotIn('RecognizeAsync', dispatch)
        visual = (APP / 'CaptureCoordinator.ImageAnalysis.cs').read_text(encoding='utf8')
        provider = (ROOT / 'src/PingYi.Infrastructure/ChatCompatibleImageAnalysisProvider.cs').read_text(encoding='utf8')
        for source in (visual, provider):
            self.assertNotIn('RecognizeAsync', source)
            self.assertNotIn('TranslateAsync', source)
            self.assertNotIn('PaddleProvider', source)

    def test_image_destination_is_frozen_and_confirmed_before_send(self):
        code = (APP / 'CaptureCoordinator.ImageAnalysis.cs').read_text(encoding='utf8')
        self.assertLess(code.index('ConfirmAsync'), code.index('CreateImageAnalyzer(snapshot)'))
        self.assertLess(code.index('VisionConfigurationChanged'), code.index('CreateImageAnalyzer(snapshot)'))
        self.assertIn('!endpoint.IsLoopback', code)

    def test_processing_deadline_includes_cpu_startup_and_cancel_action_exists(self):
        code = (APP / 'CaptureCoordinator.cs').read_text(encoding='utf8')
        self.assertIn('ManagedRuntimeReadiness.OperationTimeout', code)
        self.assertIn('CancelForWindow(window)', code)
        self.assertIn('purpose = CapturePurpose.TranslateText', code)

    def test_automatic_probe_uses_local_services_and_never_a_remote_model(self):
        code = (APP / 'CaptureCoordinator.Automatic.cs').read_text(encoding='utf8')
        self.assertIn('services.QrCodeDecoder.DecodeAsync', code)
        self.assertIn('services.PaddleProvider.RecognizeAsync', code)
        for forbidden in ('GetOcrProvider', 'CreateImageAnalyzer', 'DownloadAsync', 'HttpClient', 'OpenWebLinkAsync'):
            self.assertNotIn(forbidden, code)
        text = (APP / 'CaptureCoordinator.Text.cs').read_text(encoding='utf8')
        self.assertLess(text.index('AutomaticConsentWindow.ConfirmAsync'), text.index('ocr.GetAvailabilityAsync'))
        coordinator = (APP / 'CaptureCoordinator.cs').read_text(encoding='utf8')
        self.assertIn('requestedPurpose ?? window.RequestedPurpose', coordinator)


if __name__ == '__main__':
    unittest.main()

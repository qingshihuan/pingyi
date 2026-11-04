"""Vision integration contracts supplement the executable C# regressions."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'src/PingYi.App'


class VisionContracts(unittest.TestCase):
    def test_no_ocr_precondition_or_silent_fallback_for_image_analysis(self):
        code = (APP / 'CaptureCoordinator.cs').read_text(encoding='utf8')
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
        send = code.index('TaskImageAnalyzer(originalSettings, purpose)')
        self.assertLess(code.index('ConfigurationRouting.Resolve(originalSettings'), code.index('ConfirmAsync'))
        self.assertLess(code.index('ConfirmAsync'), send)
        self.assertLess(code.index('services.Settings != originalSettings'), send)
        self.assertLess(code.index('VisionConfigurationChanged'), send)
        self.assertIn('!endpoint.IsLoopback', code)
        factory = (ROOT / 'src/PingYi.Infrastructure/ConfiguredProviders.cs').read_text(encoding='utf8')
        self.assertIn('ModelTasks.ForPurpose(purpose)', factory)
        self.assertIn('new ProfileSecretStore(secrets, Credential(route))', factory)
        self.assertIn('route.IsManaged ? "" : route.CredentialRef', factory)

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

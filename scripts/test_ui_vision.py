"""Vision integration contracts supplement the executable C# regressions."""
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
class VisionContracts(unittest.TestCase):
    def test_no_ocr_precondition_or_silent_fallback_for_image_analysis(self):
        code = (ROOT / 'src/PingYi.App/CaptureCoordinator.cs').read_text(encoding='utf8')
        self.assertLess(code.index('await ProcessImageAnalysisAsync'), code.index('var ocrProvider ='))
        provider = (ROOT / 'src/PingYi.Infrastructure/ChatCompatibleImageAnalysisProvider.cs').read_text(encoding='utf8')
        self.assertNotIn('RecognizeAsync', provider)
        self.assertNotIn('TranslateAsync', provider)
    def test_image_destination_is_frozen_and_confirmed_before_send(self):
        code = (ROOT / 'src/PingYi.App/CaptureCoordinator.ImageAnalysis.cs').read_text(encoding='utf8')
        self.assertLess(code.index('ConfirmAsync'), code.index('CreateImageAnalyzer(snapshot)'))
        self.assertLess(code.index('VisionConfigurationChanged'), code.index('CreateImageAnalyzer(snapshot)'))
        self.assertIn('!endpoint.IsLoopback', code)
    def test_processing_deadline_includes_cpu_startup_and_cancel_action_exists(self):
        code = (ROOT / 'src/PingYi.App/CaptureCoordinator.cs').read_text(encoding='utf8')
        self.assertIn('ManagedRuntimeReadiness.OperationTimeout', code)
        self.assertIn('CancelForWindow(window)', code)
        self.assertIn('purpose = CapturePurpose.TranslateText', code)
if __name__ == '__main__': unittest.main()

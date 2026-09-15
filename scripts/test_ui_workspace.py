"""Structural UI regression checks; no OCR, accounts or model downloads."""
import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'src' / 'PingYi.App'
NS = {'a': 'https://github.com/avaloniaui'}
NAME = '{http://schemas.microsoft.com/winfx/2006/xaml}Name'


class WorkspaceStructureTests(unittest.TestCase):
    def setUp(self):
        self.settings = ET.parse(APP / 'MainWindow.axaml').getroot()
        self.home = ET.parse(APP / 'MainWindowV2.axaml').getroot()

    def test_settings_retains_backing_code_controls(self):
        expected = '''RootWindow WindowHeadingText WindowSubtitleText HelpButton
        OcrProviderCombo TranslationProviderCombo TargetLanguageCombo TranslationLanguageHintText
        InstallOcrModelsButton OcrModelDownloadIcon OcrModelInstalledIcon OcrModelButtonText OcrModelStatusText
        InstallTranslationModelsButton TranslationModelDownloadIcon TranslationModelInstalledIcon
        TranslationModelButtonText TranslationModelStatusText DeleteModelsButton ManagedModelExpander
        ManagedModelCombo ManagedRuntimeBackendCombo ManagedRuntimeBackendHintText ManagedModelSummaryText
        ManagedModelHardwareText ManagedModelProgressBar ManagedModelStatusText ManagedModelSourceButton
        ManagedModelFolderButton ManagedModelCancelButton ManagedModelStartButton ManagedModelInstallButton
        BaiduOcrApiKeyBox BaiduOcrApiKeyRevealButton BaiduOcrSecretBox BaiduOcrSecretRevealButton
        BaiduTranslateAppIdBox BaiduTranslateAppIdRevealButton BaiduTranslateSecretBox BaiduTranslateSecretRevealButton
        BaiduOcrCredentialStatusText BaiduTranslationCredentialStatusText ValidateBaiduCredentialsButton
        GoogleCloudApiKeyBox GoogleCloudApiKeyRevealButton GoogleOcrCredentialStatusText GoogleTranslationCredentialStatusText
        ValidateGoogleCredentialsButton LocalServicePresetCombo CustomEndpointBox CustomModelBox
        CustomApiKeyBox CustomApiKeyRevealButton CustomTranslationStatusText UseLocalLlamaPresetButton
        TestCustomTranslationButton TestCustomVisionButton HotkeyBox UiLanguageCombo
        StartMinimizedCheckBox CheckForUpdatesCheckBox CheckUpdatesNowButton OpenLatestReleaseButton
        UpdateStatusText GlobalStatusBorder StatusIndicator GlobalStatusText SaveSettingsButton'''.split()
        actual = {n.get(NAME) for n in self.settings.iter()}
        self.assertEqual(set(expected) - actual, set())

    def test_home_retains_status_and_capture_controls(self):
        expected = '''TopStatusText TopStatusDot LiveStatusTitleText LiveStatusDetailText LiveStatusDot
        RecoveryBorder RecoveryDetailText ModeSummaryText OcrSummaryText TranslationSummaryText
        ModePurposeText ModeRequirementsText ChooseModeButton TargetSummaryText CaptureHotkeyText ModelStatusDot ModelStatusTitleText ModelStatusDetailText CaptureButtonV2'''.split()
        self.assertTrue(set(expected) <= {n.get(NAME) for n in self.home.iter()})

    def test_named_controls_are_unique(self):
        for root in (self.settings, self.home):
            names = [n.get(NAME) for n in root.iter() if n.get(NAME)]
            self.assertEqual(len(names), len(set(names)))

    def test_five_native_categories_and_individual_scroll_regions(self):
        tabs = self.settings.findall('.//a:TabItem', NS)
        self.assertEqual(len(tabs), 5)
        for tab in tabs:
            self.assertIsNotNone(tab.find('a:ScrollViewer', NS))
            self.assertIsNone(tab.find('.//a:DataTemplate', NS))

    def test_save_and_live_feedback_do_not_scroll(self):
        for root, names in ((self.settings, {'SaveSettingsButton', 'GlobalStatusText'}),
                            (self.home, {'LiveStatusDetailText'})):
            for scroll in root.findall('.//a:ScrollViewer', NS):
                self.assertFalse(names & {n.get(NAME) for n in scroll.iter()})

    def test_six_secret_fields_keep_readonly_and_routing(self):
        names = ['BaiduOcrApiKeyBox', 'BaiduOcrSecretBox', 'BaiduTranslateAppIdBox',
                 'BaiduTranslateSecretBox', 'GoogleCloudApiKeyBox', 'CustomApiKeyBox']
        controls = {n.get(NAME): n for n in self.settings.iter()}
        for name in names:
            self.assertEqual(controls[name].get('IsReadOnly'), 'True')
        tags = {n.get('Tag') for n in self.settings.iter()
                if n.get('Click') == 'ToggleSecretVisibility_OnClick'}
        self.assertEqual(tags, {'baidu-ocr-api-key', 'baidu-ocr-secret-key', 'baidu-translate-app-id',
                               'baidu-translate-secret', 'google-cloud-api-key', 'custom-translation-api-key'})

    def test_workspace_copy_members_exist(self):
        source = (APP / 'WorkspaceText.cs').read_text(encoding='utf-8')
        defined = set(re.findall(r'public static string (\w+)\s*=>', source))
        for path in (APP / 'MainWindow.axaml', APP / 'MainWindowV2.axaml'):
            used = set(re.findall(r'WorkspaceText\.(\w+)', path.read_text(encoding='utf-8')))
            self.assertFalse(used - defined)

    def test_existing_handlers_remain_connected(self):
        expected = '''HelpButton_OnClick UiLanguageCombo_OnSelectionChanged ProviderCombo_OnSelectionChanged TargetLanguageCombo_OnSelectionChanged
        CheckStatusButton_OnClick InstallOcrModelsButton_OnClick InstallTranslationModelsButton_OnClick
        DeleteModelsButton_OnClick ManagedModelCombo_OnSelectionChanged ManagedRuntimeBackendCombo_OnSelectionChanged
        ManagedModelSourceButton_OnClick ManagedModelFolderButton_OnClick ManagedModelCancelButton_OnClick
        ManagedModelStartButton_OnClick ManagedModelInstallButton_OnClick ToggleSecretVisibility_OnClick
        ValidateBaiduCredentialsButton_OnClick ValidateGoogleCredentialsButton_OnClick UseLocalLlamaPresetButton_OnClick
        TestCustomTranslationButton_OnClick TestCustomVisionButton_OnClick CheckUpdatesNowButton_OnClick
        OpenLatestReleaseButton_OnClick SaveButton_OnClick'''.split()
        used = {n.get(a) for n in self.settings.iter() for a in ('Click', 'SelectionChanged')}
        self.assertTrue(set(expected) <= used)


if __name__ == '__main__':
    unittest.main()

"""Contract checks for the five v0.4.0 usability regressions; no network or user data."""
import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'src' / 'PingYi.App'
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'

class UsabilityStructureTests(unittest.TestCase):
    def test_locale_keys_match_and_every_dynamic_ui_text_exists(self):
        dictionaries = []
        for locale in ('zh-CN', 'en-US'):
            nodes = [node for name in ('Strings', 'Vision', 'Linux')
                     for node in ET.parse(APP / 'Localization' / f'{name}.{locale}.axaml').getroot()]
            values = {n.get(X + 'Key'): n.text for n in nodes}
            self.assertEqual(len(nodes), len(values))
            self.assertTrue(all(values.values()))
            dictionaries.append(values)
        self.assertEqual(dictionaries[0].keys(), dictionaries[1].keys())
        for file in APP.glob('*.axaml'):
            text = file.read_text(encoding='utf8')
            for key in re.findall(r'\{DynamicResource ((?:String|Text|Workspace|Polish|Linux)\.[^}]+)\}', text):
                self.assertIn(key, dictionaries[0], (file.name, key))
            self.assertNotRegex(text, r'x:Static local:(?:WorkspaceText|PolishText)\.')

    def test_only_one_capture_shell_and_legacy_preference_is_removed(self):
        app = (APP / 'App.axaml.cs').read_text(encoding='utf8')
        self.assertIn('_mainWindow = new MainWindow(', app)
        self.assertNotIn('InterfaceStyle', app)
        core = (ROOT / 'src/PingYi.Core/AppSettings.cs').read_text(encoding='utf8')
        self.assertNotIn('InterfaceStyle', core)
        settings = (APP / 'SettingsWindow.axaml').read_text(encoding='utf8')
        for name in ('InterfaceStyleCombo', 'CaptureHero', 'OpenClassicInterfaceButton'):
            self.assertNotIn(name, settings)
        self.assertFalse((APP / 'MainWindowV2.axaml').exists())

    def test_home_has_help_and_mode_picker_but_no_privacy_card_or_mode_flyout(self):
        home = (APP / 'MainWindow.axaml').read_text(encoding='utf8')
        self.assertIn('HelpButton', home)
        self.assertIn('ChooseModeButton', home)
        self.assertNotIn('PrivacySummaryText', home)
        self.assertNotIn('MenuFlyout', home)
        help_page = (APP / 'HelpAboutWindow.axaml').read_text(encoding='utf8')
        self.assertIn('PrivacySummaryText', help_page)

    def test_capture_transactions_share_one_restore_path_and_native_barrier(self):
        coordinator = (APP / 'CaptureCoordinator.cs').read_text(encoding='utf8')
        scope = (APP / 'CaptureWindowScope.cs').read_text(encoding='utf8')
        self.assertIn('desktopLifetime.Windows.ToList()', coordinator)
        self.assertIn('CaptureWindowScope.RunAsync', coordinator)
        self.assertIn('finally { IsCapturingScreen = false; }', coordinator)
        self.assertLess(scope.index('snapshot.Hide()'), scope.index('await waitForDesktop'))
        self.assertLess(scope.index('await waitForDesktop'), scope.index('await captureAndSelect'))
        self.assertIn('snapshot.Restore', scope)
        self.assertIn('IsWindowsVersionAtLeast(10, 0, 19041)', scope)
        self.assertIn('DwmFlush', scope)

    def test_language_selector_is_wired_and_never_reloads_secret_values(self):
        xaml = (APP / 'SettingsWindow.axaml').read_text(encoding='utf8')
        self.assertIn('SelectionChanged="UiLanguageCombo_OnSelectionChanged"', xaml)
        code = (APP / 'SettingsWindow.Language.cs').read_text(encoding='utf8')
        self.assertLess(code.index('await _persistLanguage(language)'), code.index('UiText.Configure(language)'))
        self.assertNotIn('RefreshCredentialStatusAsync', code)
        self.assertNotIn('LoadSettings();', code)

if __name__ == '__main__':
    unittest.main()

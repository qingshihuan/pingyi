"""Source contracts complement behavior tests without reading user data."""
import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / 'src' / 'PingYi.App'
NAME = '{http://schemas.microsoft.com/winfx/2006/xaml}Name'


class FeedbackFixStructureTests(unittest.TestCase):
    def test_removed_options_have_no_visible_controls_or_handlers(self):
        settings = (APP / 'MainWindow.axaml').read_text(encoding='utf-8')
        backing = (APP / 'MainWindow.axaml.cs').read_text(encoding='utf-8')
        self.assertNotIn('InterfaceStyleCombo', settings + backing)
        self.assertNotIn('OpenClassicInterfaceButton', settings + backing)
        self.assertNotIn('Settings.InterfaceStyle', (APP / 'App.axaml.cs').read_text(encoding='utf-8'))

    def test_home_has_help_and_a_dialog_selector_not_a_privacy_card_or_menu(self):
        home = ET.parse(APP / 'MainWindowV2.axaml').getroot()
        names = {el.get(NAME) for el in home.iter()}
        self.assertNotIn('PrivacySummaryText', names)
        self.assertIn('ChooseModeButton', names)
        self.assertTrue(any(el.get('Click') == 'HelpButton_OnClick' for el in home.iter()))
        self.assertFalse(any(el.tag.endswith(('MenuFlyout', 'MenuItem')) for el in home.iter()))
        self.assertIn('DataFlowText', {el.get(NAME) for el in ET.parse(APP / 'HelpWindow.axaml').getroot().iter()})

    def test_all_interface_resources_are_defined_and_localized(self):
        source = (APP / 'UiText.Resources.cs').read_text(encoding='utf-8')
        keys = set(re.findall(r'\["([^"]+)"\] =', source))
        for path in APP.glob('*.axaml'):
            if path.name == 'App.axaml':
                continue
            text = path.read_text(encoding='utf-8')
            used = set(re.findall(r'\{DynamicResource ((?:Ui|String)\.[^}]+)\}', text))
            self.assertFalse(used - keys, (path, used - keys))
            self.assertNotRegex(text, r'\{x:Static local:(?:WorkspaceText|PolishText)\.')
            for el in ET.parse(path).getroot().iter():
                for attr in ('Text', 'Content', 'Title', 'PlaceholderText', 'AutomationProperties.Name'):
                    value = el.get(attr, '')
                    if value and not value.startswith('{') and value != '文':
                        self.assertNotRegex(value, '[\u4e00-\u9fff]', (path, value))

    def test_capture_preparation_is_awaited_before_sampling_pixels(self):
        text = (APP / 'CaptureCoordinator.cs').read_text(encoding='utf-8')
        self.assertLess(text.index('CaptureVisibilityScope.HideAll'), text.index('visibility.WaitForDesktopAsync'))
        self.assertLess(text.index('await visibility.WaitForDesktopAsync'), text.index('CaptureDesktopAsync'))
        self.assertNotIn('Task.Delay(140', text)
        self.assertIn('visibility?.Restore(showWindows:', text)

    def test_language_change_is_connected_and_does_not_save_secret_fields(self):
        text = (APP / 'MainWindow.Language.cs').read_text(encoding='utf-8')
        self.assertIn('await PersistLanguageAsync(language)', text)
        self.assertLess(text.index('await PersistLanguageAsync(language)'), text.index('UiText.Configure(language)'))
        self.assertNotIn('SaveAllEnteredSecrets', text)
        self.assertNotIn('LoadSettings()', text)


if __name__ == '__main__':
    unittest.main()

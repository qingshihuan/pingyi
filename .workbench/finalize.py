"""Build explicit runtime resources and remove the obsolete settings controls."""
from pathlib import Path
import hashlib
import html
import json
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent.parent
app = root / 'src/PingYi.App'
p = app / 'MainWindow.axaml'
s = p.read_text(encoding='utf-8')
assert hashlib.sha256(s.encode()).hexdigest() == '33f2a9040f8ab785df373b8abec59c98894a38d2a3a062bf248fe981406a5a06'
s = re.sub(r'      <Border Grid.Column="1" x:Name="CaptureHero".*?</Border>',
    '<Button Grid.Column="1" x:Name="HelpButton" Classes="toolbar-action" Content="帮助与关于" Click="HelpButton_OnClick" VerticalAlignment="Center" AutomationProperties.Name="帮助与关于"/>', s, flags=re.S)
s = re.sub(r'                <StackPanel Spacing="6">\s*<TextBlock Text="主界面样式".*?</StackPanel>\s*<TextBlock Text="切换后重新启动屏译生效；经典完整界面会继续保留。"[^>]*/>', '', s, flags=re.S)
s = re.sub(r'            <Border Classes="workspace-card" CornerRadius="16">\s*<StackPanel Spacing="12">\s*<TextBlock Text="经典完整界面".*?</Border>', '', s, flags=re.S)
s = s.replace('AutomationProperties.Name="选择界面语言"/>', 'AutomationProperties.Name="选择界面语言" SelectionChanged="UiLanguageCombo_OnSelectionChanged"/>')
s = s.replace('跟随系统会在中文系统使用简体中文，其他系统使用 English；重新启动后生效。', '语言选择后立即保存并生效，不会丢失其他未保存的输入。')
assert 'InterfaceStyleCombo' not in s and 'OpenClassicInterfaceButton' not in s
p.write_text(s, encoding='utf-8', newline='\n')

literal = r'((?:[^"\\]|\\.)*)'
def decode(s): return json.loads('"' + s + '"')
english = {decode(a): decode(b) for a,b in re.findall(r'\["' + literal + r'"\] = "' + literal + r'"', (app/'UiText.cs').read_text(encoding='utf-8'))}
english.update(json.loads((root/'.workbench/ui-extra.json').read_text(encoding='utf-8')))
lookup = {'Ui.Text.' + hashlib.sha256(z.encode()).hexdigest()[:12]: (z,e) for z,e in english.items()}
for cls in ('WorkspaceText', 'PolishText'):
    for name,z,e in re.findall(r'public static string (\w+) => Choose\("' + literal + r'", "' + literal + r'"\)', (app/(cls+'.cs')).read_text(encoding='utf-8')):
        lookup['Ui.'+cls+'.'+name] = (decode(z),decode(e))
for el in ET.parse(app/'Localization/Strings.zh-CN.axaml').getroot():
    key = el.get('{http://schemas.microsoft.com/winfx/2006/xaml}Key')
    lookup[key] = (el.text,english[key])

attributes = re.compile(r'(\b(?:Text|Content|Header|Title|PlaceholderText|AutomationProperties.Name|ToolTip.Tip)=)"([^"]*)"')
used = set()
for p in app.glob('*.axaml'):
    if p.name == 'App.axaml': continue
    def replace(m):
        value = html.unescape(m[2])
        match = re.fullmatch(r'\{x:Static local:((?:WorkspaceText|PolishText)\.\w+)\}', value)
        if match:
            key = 'Ui.'+match[1]
        elif not value.startswith('{') and value != '文' and re.search('[\u4e00-\u9fff]',value):
            key = 'Ui.Text.'+hashlib.sha256(value.encode()).hexdigest()[:12]
        else: return m[0]
        if key not in lookup: raise ValueError('Missing bilingual text: '+value)
        return m[1]+'"{DynamicResource '+key+'}"'
    text = attributes.sub(replace,p.read_text(encoding='utf-8'))
    used.update(re.findall(r'\{DynamicResource ((?:Ui|String)\.[^}]+)\}', text))
    p.write_text(text.rstrip()+'\n',encoding='utf-8',newline='\n')
used.update(key for key in lookup if key.startswith('String.'))
missing = used - lookup.keys()
assert not missing, missing
q = lambda s: json.dumps(s,ensure_ascii=False)
entries = '\n'.join('        ['+q(k)+'] = ('+q(lookup[k][0])+', '+q(lookup[k][1])+'),' for k in sorted(used))
(app/'UiText.Resources.cs').write_text('''namespace PingYi.App;

public static partial class UiText
{
    // Bilingual runtime resources; no generated user content or credentials.
    private static readonly IReadOnlyDictionary<string, (string Chinese, string English)> UiResources =
        new Dictionary<string, (string Chinese, string English)>(StringComparer.Ordinal)
    {
'''+entries+'''
    };

    private static void ApplyLanguageResources()
    {
        if (Avalonia.Application.Current?.Resources is not { } resources) return;
        foreach (var (key, pair) in UiResources)
            resources[key] = IsEnglish ? pair.English : pair.Chinese;
    }

    private static readonly Lazy<IReadOnlyDictionary<string, string>> CanonicalLabels = new(() =>
        EnglishText.Select(p => (En: p.Value, Zh: p.Key))
            .Concat(UiResources.Values.Select(p => (En: p.English, Zh: p.Chinese)))
            .GroupBy(p => p.En, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Zh, StringComparer.Ordinal));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ResourceTranslations = new(() =>
        UiResources.Values.GroupBy(p => p.Chinese, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().English, StringComparer.Ordinal));

    private static string CanonicalLabel(string text) => CanonicalLabels.Value.TryGetValue(text, out var value) ? value : text;
    private static string? ResourceTranslation(string text) => ResourceTranslations.Value.TryGetValue(text, out var value) ? value : null;
}
''',encoding='utf-8',newline='\n')
print('Built', len(used), 'explicit bilingual UI resources.')

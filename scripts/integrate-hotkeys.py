"""One-use integration of reviewed edits; removes itself before the final commit."""
from pathlib import Path


def edit(path, before, after, count=1):
    target = Path(path)
    text = target.read_text(encoding='utf-8')
    found = text.count(before)
    if found != count:
        raise RuntimeError(f'{path}: expected {count} anchors, found {found}')
    target.write_text(text.replace(before, after), encoding='utf-8')


path = 'src/PingYi.App/SettingsWindow.axaml.cs'
edit(path, '    private bool _isLoadingSettings;', '    private bool _isLoadingSettings;\n    private bool _isSavingSettings;')
edit(path, '            HotkeyBox.Text = settings.Hotkey;', '            HotkeyBox.Text = settings.Hotkey;\n            _useDefaultHotkey = !settings.HotkeyIsCustomized;')
text = Path(path).read_text(encoding='utf-8')
start = text.index('    private async void SaveButton_OnClick(')
end = text.index('    private async void ProviderCombo_OnSelectionChanged(', start)
text = text[:start] + '''    private async void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null || _hotkeyEditorBusy || _isSavingSettings) return;
        _isSavingSettings = true;
        RecordHotkeyButton.IsEnabled = ResetHotkeyButton.IsEnabled = HotkeyBox.IsEnabled = false;
        var button = sender as Button;
        BeginButtonOperation(button, "正在保存…");
        SetGlobalStatus("正在保存设置与安全凭据…", isError: false);
        try
        {
            var updatedSettings = BuildSettingsFromForm();
            await SaveAllEnteredSecretsAsync();
            await _services.ApplyHotkeySettingsAsync(updatedSettings);
            HotkeyBox.Text = _services.Settings.Hotkey;
            _useDefaultHotkey = !_services.Settings.HotkeyIsCustomized;
            ClearSecretInputs();
            await RefreshCredentialStatusAsync();
            var secretStatus = _services.SecretStore is PlatformSecretStore { IsPersistent: false }
                ? "Linux 密钥服务不可用，凭据仅保存到本次运行结束。"
                : "敏感凭据已写入系统安全存储。";
            SetGlobalStatus(LinuxDesktopSession.IsWayland
                ? (UiText.IsEnglish ? "Preferred shortcut saved. " : "首选快捷键已保存。") + LinuxDesktopUi.ExternalShortcutHelp
                : $"设置已保存。{secretStatus}", isError: false);
            FinishButtonOperation(button, "已保存并应用", success: true);
        }
        catch (Exception exception)
        {
            SetGlobalStatus(LinuxDesktopUi.DescribeError(exception), isError: true);
            FinishButtonOperation(button, "保存失败", success: false);
        }
        finally
        {
            _isSavingSettings = false;
            RecordHotkeyButton.IsEnabled = ResetHotkeyButton.IsEnabled = HotkeyBox.IsEnabled = true;
            RefreshHotkeyEditor();
        }
    }

''' + text[end:]
Path(path).write_text(text, encoding='utf-8')
edit(path, '            Hotkey = HotkeyBox.Text ?? AppSettings.DefaultHotkey,', '            Hotkey = GlobalHotkeyGesture.Parse(HotkeyBox.Text ?? string.Empty).ToString(),\n            HotkeyIsCustomized = !_useDefaultHotkey,')

path = 'src/PingYi.App/AppServices.Hotkeys.cs'
edit(path, '    public async Task ApplyHotkeySettingsAsync(AppSettings settings)', '''    public async Task StartConfiguredHotkeyAsync()
    {
        await _hotkeyEditGate.WaitAsync();
        try
        {
            await HotkeyService.StartAsync(Settings.Hotkey);
            HotkeyRegistrationError = null;
        }
        catch (Exception error) { HotkeyRegistrationError = error; throw; }
        finally { _hotkeyEditGate.Release(); }
    }

    public async Task ApplyHotkeySettingsAsync(AppSettings settings)''')

path = 'src/PingYi.App/App.axaml.cs'
edit(path, '            _mainShell = (IMainWindowShell)_mainWindow;\n            _mainWindow.Closing', '            _mainShell = (IMainWindowShell)_mainWindow;\n            _services.HotkeyChanged += OnHotkeyChanged;\n            _mainWindow.Closing')
edit(path, '                Dispatcher.UIThread.Post(() => _ = _captureCoordinator.StartCaptureAsync(_mainShell));', '''                Dispatcher.UIThread.Post(() =>
                {
                    if (!_services.IsEditingHotkey && !_isExiting)
                        _ = _captureCoordinator.StartCaptureAsync(_mainShell);
                });''')
edit(path, 'await _services!.HotkeyService.StartAsync(_services.Settings.Hotkey);', 'await _services!.StartConfiguredHotkeyAsync();')
edit(path, '''                case "capture" when _captureCoordinator is not null && _mainShell is not null:
                    await _captureCoordinator.StartCaptureAsync(_mainShell);
                    break;''', '''                case "capture":
                    if (_captureCoordinator is not null && _mainShell is not null && _services?.IsEditingHotkey != true)
                        await _captureCoordinator.StartCaptureAsync(_mainShell);
                    break;''')
edit(path, '            await _services.DisposeAsync();', '            _services.HotkeyChanged -= OnHotkeyChanged;\n            await _services.DisposeAsync();')

path = 'src/PingYi.App/ShortcutRecorderWindow.cs'
edit(path, '    private string? _candidate;', '    private string? _candidate;\n    private readonly HashSet<Key> _pressed = [];')
edit(path, '        Height = 270;', '        Height = 320;')
edit(path, '        e.Handled = true;\n        if (e.Key is Key.LeftCtrl', '        e.Handled = true;\n        _pressed.Add(e.Key);\n        if (e.Key is Key.LeftCtrl')
edit(path, '''        if (_candidate is null) return;
        e.Handled = true;
        if (e.KeyModifiers == KeyModifiers.None) Close(_candidate);''', '''        _pressed.Remove(e.Key);
        if (_candidate is null) return;
        e.Handled = true;
        if (_pressed.Count == 0 && e.KeyModifiers == KeyModifiers.None) Close(_candidate);''')

# Existing regression assertions must now expect the intentionally changed Linux default.
path = 'tests/PingYi.Core.Tests/LinuxDesktopTests.cs'
edit(path, '[InlineData(true, 8, "Ctrl+Alt+D", "Ctrl+Alt+Shift+D")]', '[InlineData(true, 8, "Ctrl+Alt+D", "Ctrl+Shift+D")]')
edit(path, '"ctrl+alt+shift+d"', 'AppSettings.LinuxDefaultHotkey.ToLowerInvariant()')
path = 'scripts/test-linux-desktop.sh'
edit(path, 'xdotool key --clearmodifiers ctrl+alt+shift+d', 'xdotool key --clearmodifiers ctrl+shift+d')
with Path(path).open('a', encoding='utf-8') as file:
    file.write('\n# Inherit the isolated synthetic desktop and exercise settings persistence.\nsource scripts/test-linux-hotkey-editor.sh\n')

# Current documentation changes; historical release notes remain historical.
for path in ('README.md', 'README.en.md', 'docs/LINUX_CAPTURE.md'):
    target = Path(path)
    text = target.read_text(encoding='utf-8').replace('Ctrl+Alt+Shift+D', 'Ctrl+Shift+D')
    if path.startswith('README'):
        text += ('\n## Capture shortcut customization (v0.5.2)\n\n'
                 'Linux: `Ctrl+Shift+D`; Windows: `Ctrl+Alt+D`. Open **Settings → Appearance & startup** '
                 'to type/record a combination or restore the default, then select **Save and apply**. '
                 'Wayland requires a matching desktop custom shortcut; saving a preference is not global registration. '
                 '[Shortcut guide and conflict notes](docs/HOTKEYS.md).\n') if path == 'README.en.md' else (
                 '\n## 自定义截图快捷键（v0.5.2）\n\nLinux 默认 `Ctrl+Shift+D`，Windows 保持 `Ctrl+Alt+D`。'
                 '在 **设置 → 外观与启动** 中手动输入或点击 **录入快捷键 / 恢复默认**，最后 **保存并应用**。'
                 'Wayland 还需在桌面系统绑定同一组合；应用保存首选组合不等于系统已注册。'
                 'Chrome 使用 `Ctrl+Shift+D` 收藏所有标签页，存在应用快捷键覆盖的可能。'
                 '[完整说明、迁移及冲突处理](docs/HOTKEYS.md)。\n')
    else:
        text += '\n快捷键的录入、恢复默认、schema 10 迁移及 Wayland 手动绑定步骤见 [HOTKEYS.md](HOTKEYS.md)。\n'
    target.write_text(text, encoding='utf-8')
with Path('THIRD_PARTY_NOTICES.md').open('a', encoding='utf-8') as file:
    file.write('\n## Shortcut editor (v0.5.2)\n\nThe shortcut recorder uses the existing Avalonia input and control APIs. '
               'No additional third-party library or background keyboard-logging component is bundled. '
               'Wayland shortcut bindings remain under the desktop settings service; application preferences do not alter system bindings.\n')
Path('.github/release-version.txt').write_text('0.5.2\n', encoding='utf-8')

# Never merge one-use transfer/integration machinery into the product.
Path('.github/workflows/integrate-hotkeys.yml').unlink()
Path(__file__).unlink()

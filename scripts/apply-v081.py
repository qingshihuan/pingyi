"""One-use deterministic integration; removed before the tested commit is published."""
from pathlib import Path


def edit(path, before, after, count=1):
    target = Path(path)
    text = target.read_text(encoding='utf-8')
    if text.count(before) != count:
        raise RuntimeError(f'{path}: expected {count} exact anchors, found {text.count(before)}')
    target.write_text(text.replace(before, after), encoding='utf-8')

p = 'src/PingYi.Core/ManagedMultimodalModels.cs'
edit(p, 'Auto detect (recommended)', 'Automatic (optional)')
edit(p, 'GPU · Vulkan', 'GPU · Vulkan (default)')
edit(p, '自动检测（推荐）', '自动检测（可选）')
edit(p, '通用显卡 · Vulkan', '通用显卡 · Vulkan（默认）')
edit(p, '    public static IReadOnlyList<ManagedRuntimeBackend> All { get; } = [Auto, Cuda12, Cuda13, Rocm, Vulkan, Cpu];',
     '    public static ManagedRuntimeBackend Default => Vulkan;\n    public static IReadOnlyList<ManagedRuntimeBackend> All { get; } = [Vulkan, Auto, Cuda12, Cuda13, Rocm, Cpu];')
edit(p, '        : Auto.Id;', '        : Default.Id;')
p = 'src/PingYi.Core/AppSettings.cs'
edit(p, 'CurrentSchemaVersion = 12', 'CurrentSchemaVersion = 13')
edit(p, '= ManagedRuntimeBackends.Auto.Id;', '= ManagedRuntimeBackends.Default.Id;')
edit(p, '            ManagedRuntimeBackend = ManagedRuntimeBackends.Normalize(ManagedRuntimeBackend),',
'''            // Migrate the former automatic default only without an explicit GPU choice.
            // Explicit CPU/CUDA/ROCm/Vulkan selections and schema-13 Auto stay unchanged.
            ManagedRuntimeBackend = SchemaVersion < 13 &&
                ManagedRuntimeBackends.Normalize(ManagedRuntimeBackend) == "auto" &&
                RuntimeDeviceChoice.Normalize(ManagedRuntimeDevice) == "auto"
                    ? ManagedRuntimeBackends.Default.Id : ManagedRuntimeBackends.Normalize(ManagedRuntimeBackend),''')
p = 'src/PingYi.Infrastructure/ManagedModelService.cs'
edit(p, 'public sealed class ManagedModelService', 'public sealed partial class ManagedModelService')
edit(p, '            ManagedRuntimeBackends.Auto.Id,', '            ManagedRuntimeBackends.Default.Id,')
edit(p, 'if (_ownedProcess is null && deviceSelection != "auto")',
     'if (_ownedProcess is null && (deviceSelection != "auto" || normalizedBackend != ManagedRuntimeBackends.Auto.Id))')
edit(p, '无法为它更换执行显卡；请在该服务中设置或停止它后重试。',
     '无法为它更换后端或执行显卡；请在该服务中设置或停止它后重试。')
edit('src/PingYi.Infrastructure/Runtime/RuntimeManager.cs', 'public sealed class RuntimeManager', 'public sealed partial class RuntimeManager')
p = 'src/PingYi.App/AppServices.cs'
edit(p, '        if (Volatile.Read(ref _disposeState) != 0) return;\n        if (!AppSettings.TryParseChatCompletionsEndpoint',
'''        if (Volatile.Read(ref _disposeState) != 0) return;
        if (IsRuntimeMaintenance) throw new ProviderException("runtime_maintenance", "正在切换／卸载后端，请完成后再保存设置。 / Finish runtime maintenance before saving settings.");
        if (!AppSettings.TryParseChatCompletionsEndpoint''')
edit(p, '        while (true)\n        {\n            if (Volatile.Read(ref _disposeState) != 0)',
'''        while (true)
        {
            if (IsRuntimeMaintenance) throw new ProviderException("runtime_maintenance", "正在切换运行后端，请稍后重试。 / Runtime maintenance in progress; retry shortly.");
            if (Volatile.Read(ref _disposeState) != 0)''')
edit(p, '''    private void WarmManagedRuntimeIfConfigured(bool forImageAnalysis = false)
    {
        if (Volatile.Read(ref _disposeState) != 0) return;''',
'''    private void WarmManagedRuntimeIfConfigured(bool forImageAnalysis = false)
    {
        if (Volatile.Read(ref _disposeState) != 0 || IsRuntimeMaintenance) return;''')
p = 'src/PingYi.App/AppServices.InitialSetup.cs'
edit(p, '''            await ManagedModels.Runtimes.InstallAsync(backend, device, allowMirrors,
                mirrorPrefixes.Split('\\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), progress, cancellation);''',
'''            // Installed Vulkan/CPU do not require an online update just to configure a model.
            if (await ManagedModels.Runtimes.RecommendedInstalledAsync(backend, device, cancellation) is null)
                await ManagedModels.Runtimes.InstallAsync(backend, device, allowMirrors,
                    mirrorPrefixes.Split('\\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), progress, cancellation);''')
target = Path(p); text = target.read_text()
a = text.index('            using var verification = CancellationTokenSource.CreateLinkedTokenSource(cancellation);')
b = text.index('            cancellation.ThrowIfCancellationRequested();', a)
block = text[a:b]
text = text[:a] + '            await VerifyConfiguredModelAsync(configured, cancellation);\n' + text[b:]
method = '\n    private async Task VerifyConfiguredModelAsync(AppSettings configured, CancellationToken cancellation)\n    {\n' + '\n'.join(line[4:] for line in block.rstrip().split('\n')) + '\n    }\n'
text = text.rstrip()[:-1] + method + '}\n'
target.write_text(text)
p = 'src/PingYi.App/FirstRunSetupWindow.cs'
edit(p, '_backends.SelectedItem = ManagedRuntimeBackends.Auto;', '_backends.SelectedItem = ManagedRuntimeBackends.Default;')
edit(p, '?.Id ?? "auto", progress, _operation.Token', '?.Id ?? ManagedRuntimeBackends.Default.Id, progress, _operation.Token')
edit(p, '        _runtimeHardware = new RuntimeSetupPanel(', '        _backends.SelectedItem = ManagedRuntimeBackends.Get(services.Settings.ManagedRuntimeBackend);\n        _runtimeHardware = new RuntimeSetupPanel(')
edit(p, '            () => services.ManagedModels.CurrentRuntimeDescription);', '            () => services.ManagedModels.CurrentRuntimeDescription, services);')
edit(p, '        _runtimeHost.Children.Add(_runtimeHardware);', '''        _runtimeHost.Children.Add(_runtimeHardware);
        _runtimeHardware.ActivityChanged += busy =>
        {
            DownloadButton.IsEnabled = !busy && _hasRuntime;
            LightweightButton.IsEnabled = ExistingButton.IsEnabled = !busy;
        };''')
p = 'src/PingYi.App/SettingsWindow.Models.cs'
edit(p, '?? ManagedRuntimeBackends.Auto', '?? ManagedRuntimeBackends.Default', count=2)
edit(p, 'if (_services is null || _managedModelOperation is not null || ManagedModelCombo.SelectedItem',
     'if (_services is null || _managedModelOperation is not null || _runtimeHardware?.IsBusy == true || ManagedModelCombo.SelectedItem')
p = 'src/PingYi.App/RuntimeSetupPanel.cs'
edit(p, 'internal sealed class RuntimeSetupPanel', 'internal sealed partial class RuntimeSetupPanel')
edit(p, 'Func<string>? running = null)', 'Func<string>? running = null, AppServices? services = null)')
edit(p, '        SetDeviceChoices([], null);\n        Children.Add(new TextBlock', '        InitializeManagement(services);\n        SetDeviceChoices([], null);\n        Children.Add(new TextBlock')
edit(p, '        Children.Add(_inventory);', '        Children.Add(_inventory);\n        Children.Add(ManagementStatus);')
edit(p, 'Children = { DetectButton, InstallButton, _cancel }', 'Children = { DetectButton, SwitchButton, InstallButton, UninstallButton, DefaultButton, _cancel }')
edit(p, '        _selection = "auto";\n        await RefreshAsync();', '        _selection = "auto"; _removalConfirmation = null;\n        SetDeviceChoices([], null);\n        SetControls();\n        await RefreshAsync();')
edit(p, '        _cancel.IsVisible = _work is not null;', '        SetManagementControls(ready);\n        _cancel.IsVisible = _work is not null;')
edit(p, '        try { await RefreshCoreAsync(_work.Token); }', '''        var backendEnabled = _backend.IsEnabled;
        _backend.IsEnabled = false;
        try { await RefreshCoreAsync(_work.Token); }''')
edit(p, 'catch (Exception error) { if (!_closed) _status.Text = Pick("设备检测未完成：", "Device detection did not complete: ") + UiText.Error(error); }',
     'catch (Exception error) { if (!_closed) { SetDeviceChoices([], null); ManagementStatus.Text = _status.Text = Pick("设备检测未完成：", "Device detection did not complete: ") + UiText.Error(error); } }')
edit(p, 'finally { _work.Dispose(); _work = null; if (version == _scanVersion) SetControls(); }',
     'finally { _work.Dispose(); _work = null; _backend.IsEnabled = backendEnabled; if (version == _scanVersion) SetControls(); }')
edit(p, '?.Id ?? "auto"', '?.Id ?? ManagedRuntimeBackends.Default.Id', count=2)
edit(p, '            _status.Text = Pick("该后端尚未安装。', '            ManagementStatus.Text = _status.Text = Pick("该后端尚未安装。')
edit(p, '        SetDeviceChoices(devices, runtime.Backend);', '''        SetDeviceChoices(devices, runtime.Backend);
        ManagementStatus.Text = runtime.Backend != "cpu" && !devices.Any(d => d.IsHardwareGpu)
            ? Pick("后端已安装，但没有可用显卡。请检查依赖和驱动，或返回 Vulkan；没有把自动选项当作可运行证明。", "Installed, but no usable GPU. Check dependencies/drivers or return to Vulkan; the Auto choice is not proof of GPU support.")
            : Pick("已保存：", "Saved: ") + ManagedRuntimeBackends.Get(_currentSettings?.Invoke().ManagedRuntimeBackend).LocalizedDisplayName + $" · {runtime.Backend} · {runtime.Tag} · " + Pick("仅选择不会切换；请点击安装并切换。内置 Vulkan／CPU 不卸载，模型保留。", "Selection alone does not switch; click Install and switch. Bundled Vulkan/CPU and model weights are retained.");''')
edit(p, '            _services.Settings, () => _services.ManagedModels.CurrentRuntimeDescription);',
     '            _services.Settings, () => _services.ManagedModels.CurrentRuntimeDescription, _services);')
edit(p, '        parent.Children.Insert(parent.Children.IndexOf(ManagedRuntimeBackendHintText) + 1, _runtimeHardware);',
'''        parent.Children.Insert(parent.Children.IndexOf(ManagedRuntimeBackendHintText) + 1, _runtimeHardware);
        var controls = new Control[] { SaveSettingsButton, ManagedModelCombo, ManagedModelInstallButton, ManagedModelStartButton };
        bool[]? enabled = null;
        _runtimeHardware.ActivityChanged += busy =>
        {
            if (busy) { enabled = controls.Select(c => c.IsEnabled).ToArray(); foreach (var c in controls) c.IsEnabled = false; }
            else if (enabled is not null) { for (var i = 0; i < controls.Length; i++) controls[i].IsEnabled = enabled[i]; enabled = null; }
        };''')
edit('tests/PingYi.Core.Tests/ManagedMultimodalModelsTests.cs', '[InlineData("cuda", "auto")]', '[InlineData("cuda", "vulkan")]')
p = 'src/PingYi.App/SettingsWindow.axaml.cs'
edit(p, '            var updatedSettings = BuildSettingsFromForm();', '''            var updatedSettings = BuildSettingsFromForm();
            if (updatedSettings.ManagedRuntimeBackend != previousSettings.ManagedRuntimeBackend ||
                updatedSettings.ManagedRuntimeDevice != previousSettings.ManagedRuntimeDevice)
                throw new ProviderException("runtime_apply_required", "更换后端／显卡请先点击“安装并切换到所选后端”，成功后再保存其他设置。 / Use Install and switch for backend/GPU changes before saving other settings.");''')
edit('tests/PingYi.Core.Tests/SettingsCompatibilityTests.cs', 'Assert.Equal("auto", settings.ManagedRuntimeBackend);', 'Assert.Equal("vulkan", settings.ManagedRuntimeBackend);')
edit('README.md', '自动后端优先尝试 Vulkan，失败时回退 CPU；显式选 Vulkan 时不自动切换。', '默认使用内置 Vulkan；需要时手动切换 CUDA／ROCm 或 CPU。显式选 Vulkan 时不自动切换。')
edit('README.en.md', 'Auto tries Vulkan and falls back to CPU; explicit Vulkan does not automatically switch.', 'Bundled Vulkan is the default; CUDA/ROCm or CPU can be selected manually. Explicit Vulkan does not automatically fall back.')
for path, heading, section in [
    ('README.md', '## 首次使用', '## 后端切换与卸载（v0.8.1）\n\n默认使用 Vulkan。进入 **设置 → 本地模型 → 显卡与运行后端**，选择 ROCm／CUDA 等后端和执行显卡，点击 **安装并切换到所选后端**。先安装、启动和设备检查，再以已配置模型验证 OCR／翻译；成功才保存，失败保留原配置并尝试恢复旧进程。更换后端不重复下载模型。\n\n成功切换后，可选择旧后端并连续两次点击 **卸载所选已下载后端**，清理它的下载版本和未共享缓存。正在使用的后端不能卸载；安装包内置 Vulkan／CPU 保留作为恢复基础，停用后不占显存。模型、配置、驱动和外部服务不删除。未安装／无可用设备的原因直接显示在显卡列表旁。详见 [后端管理](docs/RUNTIME_SWITCHING.md)。\n\n'),
    ('README.en.md', '## First run', '## Switch and uninstall runtimes (v0.8.1)\n\nVulkan is the default. In **Settings → Local models → GPU and inference runtime**, choose a backend/device and select **Install and switch to selected backend**. Installation and device checks precede activation; an already-configured model must pass OCR/translation verification before preferences are saved. Failures retain the previous settings and attempt to restore its server. Model weights are reused.\n\nAfter switching, select the old backend and confirm **Uninstall selected downloaded backend** with a second click. Active backends are protected; bundled Vulkan/CPU, models, settings, drivers and external services are retained. Only downloaded backend versions and unshared caches are removed. Bundled files do not consume VRAM when unused. See [runtime management](docs/RUNTIME_SWITCHING.md).\n\n')]:
    edit(path, heading, section + heading)
with Path('THIRD_PARTY_NOTICES.md').open('a', encoding='utf-8') as output:
    output.write('\n## Vulkan default and manual runtime management (v0.8.1)\n\nThis change reuses the existing Avalonia, .NET and llama.cpp integrations; no additional production dependency or SDK is bundled. Only application-downloaded runtime versions and their unshared verified archive caches are eligible for removal. Bundled Vulkan/CPU, model weights, system drivers and external model services are not uninstalled. Existing CUDA/ROCm redistribution and driver requirements remain applicable; backend switching is not hardware certification.\n')
Path('scripts/apply-v081.py').unlink()
Path('.github/workflows/integrate-v081.yml').unlink()

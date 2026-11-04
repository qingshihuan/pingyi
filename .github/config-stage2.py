"""One-shot, exact-baseline integration; remove after its tested patch is committed."""
from pathlib import Path
root = Path(__file__).resolve().parents[1]
def patch(path, old, new):
    target = root / path
    source = target.read_text(encoding="utf-8")
    if old not in source:
        raise SystemExit("Baseline mismatch: " + path)
    target.write_text(source.replace(old, new), encoding="utf-8", newline="\n")

patch('src/PingYi.Core/RuntimePolicy.cs', '''    public static bool UsesManagedRuntime(AppSettings settings) => HasConfiguredManagedRuntime(settings) &&
        new[] { ModelTasks.Ocr, ModelTasks.Translation }.Any(task =>
            settings.ModelConfiguration?.Tasks.Get(task).ServiceId == ModelConfiguration.Managed ||
            string.IsNullOrEmpty(settings.ModelConfiguration?.Tasks.Get(task).ServiceId) &&
            (task == ModelTasks.Ocr ? settings.OcrProviderId == "local-vlm-ocr" : settings.TranslationProviderId == "custom-chat") &&
            settings.CustomTranslationEndpoint == AppSettings.ManagedModelEndpoint);''', '''    public static bool UsesManagedRuntime(AppSettings settings) =>
        UsesManagedRuntimeForTask(settings, ModelTasks.Ocr) || UsesManagedRuntimeForTask(settings, ModelTasks.Translation);

    public static bool UsesAnyManagedRuntime(AppSettings settings) =>
        ModelTasks.All.Any(task => UsesManagedRuntimeForTask(settings, task));

    /// <summary>A task that does not use the owned runtime must not load it for another task.</summary>
    public static bool UsesManagedRuntimeForTask(AppSettings settings, string task)
    {
        if (!ModelTasks.All.Contains(task)) throw new ArgumentOutOfRangeException(nameof(task));
        if (!HasConfiguredManagedRuntime(settings)) return false;
        var id = settings.ModelConfiguration?.Tasks.Get(task).ServiceId ?? "";
        if (id.Length > 0) return id == ModelConfiguration.Managed;
        if (!string.Equals(AppSettings.NormalizeChatCompletionsEndpoint(settings.CustomTranslationEndpoint),
                AppSettings.ManagedModelEndpoint, StringComparison.OrdinalIgnoreCase)) return false;
        return task switch
        {
            ModelTasks.Ocr => settings.OcrProviderId == "local-vlm-ocr",
            ModelTasks.Translation => settings.TranslationProviderId == "custom-chat",
            _ => true
        };
    }''')
patch('src/PingYi.Infrastructure/BrowserTranslationService.cs', '''        if (RuntimePolicy.UsesManagedRuntime(settings)) await ensureRuntime(token);
        if (image is not null)
        {
            var ocr = Ocr(settings);''', '''        var runtimeReady = false;
        if (image is not null)
        {
            if (RuntimePolicy.UsesManagedRuntimeForTask(settings, ModelTasks.Ocr))
            {
                await ensureRuntime(token);
                runtimeReady = true;
            }
            var ocr = Ocr(settings);''')
patch('src/PingYi.Infrastructure/BrowserTranslationService.cs', '''        if (source == target) return new(true, Original: text, Translation: text, SourceLanguage: source, TargetLanguage: target);
        if (!(await provider.GetAvailabilityAsync(token)).IsAvailable)''', '''        if (source == target) return new(true, Original: text, Translation: text, SourceLanguage: source, TargetLanguage: target);
        if (!runtimeReady && RuntimePolicy.UsesManagedRuntimeForTask(settings, ModelTasks.Translation))
            await ensureRuntime(token);
        if (!(await provider.GetAvailabilityAsync(token)).IsAvailable)''')
for path in ('src/PingYi.App/AppServices.cs','src/PingYi.App/AppServices.Configuration.cs'):
    patch(path, '''            if (RuntimePolicy.UsesManagedRuntime(Settings)) WarmManagedRuntimeIfConfigured();
            else if (RuntimePolicy.UsesManagedRuntime(previous))''', '''            // Saving configuration is not a request to load a multi-gigabyte model.
            // Remaining managed tasks start it on demand. Retire an unused owned runtime.
            if (!RuntimePolicy.UsesAnyManagedRuntime(Settings) && RuntimePolicy.UsesAnyManagedRuntime(previous))''')
patch('src/PingYi.App/ConfigurationCenterView.cs','internal sealed class ConfigurationCenterView : UserControl', 'internal sealed partial class ConfigurationCenterView : UserControl')
patch('src/PingYi.App/ConfigurationCenterView.cs', '''        var tabs = new TabControl { ItemsSource = new[]
        {
            Tab("模型服务与模型", "Services and models", servicePage),
            Tab("任务配置", "Task routing", _routing),
            Tab("运行环境与隐私", "Runtime and privacy", environment)
        } };
        Content = new StackPanel { Spacing = 12, Children = { tabs, _cancel, _status } };''', '''        _tabs.ItemsSource = new[]
        {
            Tab("配置概览", "Overview", CreateOverviewPage()),
            Tab("服务与模型", "Services", servicePage),
            Tab("任务配置", "Tasks", _routing),
            Tab("运行与隐私", "Runtime", environment)
        };
        _tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, _tabs)) RefreshOverview(); };
        Content = new StackPanel { Spacing = 12, Children = { _tabs, _cancel, _status } };''')
patch('src/PingYi.App/ConfigurationCenterView.cs', '''        _model.SelectionChanged += (_, _) => RefreshCapabilities();
        _key.PropertyChanged''', '''        _model.SelectionChanged += (_, _) => RefreshCapabilities();
        _model.PropertyChanged += (_, e) => { if (!_loading && e.Property == ComboBox.TextProperty) RefreshCapabilities(); };
        _clearKey.PropertyChanged += (_, e) => { if (!_loading && e.Property == CheckBox.IsCheckedProperty) RefreshCapabilities(); };
        _key.PropertyChanged''')
patch('src/PingYi.App/ConfigurationCenterView.cs', '''            Baseline = settings.ModelConfiguration;
            _configuration''', '''            Baseline = settings.ModelConfiguration;
            _baselineSettings = settings;
            _configuration''')
patch('src/PingYi.App/ConfigurationCenterView.cs', '''        RefreshCapabilities();
    }

    private void SelectEditor()''', '''        RefreshCapabilities();
        RefreshOverview();
    }

    private void SelectEditor()''')
patch('src/PingYi.App/ConfigurationCenterView.cs', '''        var entry = !string.IsNullOrWhiteSpace(_key.Text) || _clearKey.IsChecked == true ? null : _editing?.Models.FirstOrDefault(m => m.Id == _model.Text);''', '''        var entry = CurrentEditorProfile()?.Models.FirstOrDefault(m => m.Id == _model.Text);''')
patch('src/PingYi.App/ConfigurationCenterView.cs', '''            _pendingKeys.TryGetValue(profile.CredentialRef, out var draftKey);
            _request = new CancellationTokenSource(); SetBusy(true);''', '''            _pendingKeys.TryGetValue(profile.CredentialRef, out var draftKey);
            if (vision is not null)
            {
                profile = profile with { Models = profile.Models.Select(m => m.Id != profile.DefaultModel ? m :
                    vision.Value ? m with { VisionVerified = false } : m with { TextVerified = false }).ToArray() };
                _profiles[_profiles.FindIndex(p => p.Id == profile.Id)] = profile;
                _editing = profile;
                RefreshCapabilities();
            }
            _request = new CancellationTokenSource(); SetBusy(true);''')
patch('src/PingYi.App/ConfigurationCenterView.cs', '''                var ids = await Discover(profile, draftKey, _request.Token);
                if (_closed) return;''', '''                var ids = await Discover(profile, draftKey, _request.Token);
                _request.Token.ThrowIfCancellationRequested();
                if (_closed) return;''')
patch('src/PingYi.App/ConfigurationCenterView.cs', '''                var entry = await Verify(profile, profile.DefaultModel, draftKey, vision.Value, _request.Token);
                if (_closed) return;''', '''                var entry = await Verify(profile, profile.DefaultModel, draftKey, vision.Value, _request.Token);
                _request.Token.ThrowIfCancellationRequested();
                if (_closed) return;''')
patch('src/PingYi.App/ConfigurationCenterView.cs', '''        finally { _request?.Dispose(); _request = null; SetBusy(false); }''', '''        finally
        {
            _request?.Dispose(); _request = null; SetBusy(false);
            if (!_closed) { RefreshCapabilities(); RefreshOverview(); }
        }''')
patch('tests/PingYi.App.Tests/ConfigurationCenterUiTests.cs', 'tabs.SelectedIndex = 1; Flush(window);', 'tabs.SelectedIndex = 2; Flush(window);')
patch('tests/PingYi.App.Tests/ConfigurationCenterUiTests.cs', 'page < 3; page++', 'page < 4; page++')

additions = {
    'README.md': '\n\n## 配置中心重构（开发分支）\n\n配置中心支持多服务独立密钥、每任务模型绑定、草稿概览和固定样例能力测试。旧配置自动迁移并备份；保存不预加载模型，不静默跨云回退。此功能尚未进入 v0.8.4 安装包。详见 [配置中心使用与迁移](docs/CONFIGURATION_CENTER.md)。\n',
    'README.en.md': '\n\n## Configuration center (development branch)\n\nThe configuration-center branch adds independent service credentials, per-task model routing, a draft overview and explicit sample tests. Legacy settings are migrated with a backup. Saving does not preload a model, and there is no silent cross-cloud fallback. This is not in the v0.8.4 installer. See [Configuration center usage and migration](docs/CONFIGURATION_CENTER.md).\n',
    'THIRD_PARTY_NOTICES.md': '\n\n## Configuration center integration\n\nThe schema-15 configuration center reuses the existing Avalonia, .NET and model-provider adapters. It introduces no bundled models, runtime binaries, fonts or third-party dependencies. Explicit provider model-list/sample-test requests may contact the configured service and incur its charges; passive configuration overview does not.\n'
}
for path, addition in additions.items():
    target = root / path
    target.write_text(target.read_text(encoding='utf-8').rstrip() + addition, encoding='utf-8', newline='\n')
print('Applied exact configuration continuation patch.')

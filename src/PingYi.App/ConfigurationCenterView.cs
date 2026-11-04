using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using PingYi.Core;

namespace PingYi.App;

/// <summary>A draft editor. Loading, navigation and localization never contact a model service.</summary>
internal sealed partial class ConfigurationCenterView : UserControl
{
    private readonly List<(Action<string> Set, string Zh, string En)> _labels = [];
    private readonly ComboBox _services = Field<ComboBox>("ProfileSelector");
    private readonly TextBox _name = Field<TextBox>("ProfileName");
    private readonly TextBox _endpoint = Field<TextBox>("ProfileEndpoint");
    private readonly ComboBox _protocol = Field<ComboBox>("ProfileProtocol");
    private readonly ComboBox _preset = Field<ComboBox>("ProfilePreset");
    private readonly ComboBox _model = Field<ComboBox>("ProfileModel");
    private readonly TextBox _key = Field<TextBox>("ProfileKey");
    private readonly CheckBox _clearKey = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _capabilities = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _editor = new() { Spacing = 10 };
    private readonly Dictionary<string, string> _pendingKeys = new(StringComparer.Ordinal);
    private readonly List<ModelServiceProfile> _profiles = [];
    private ModelConfiguration _configuration = new();
    private ModelServiceProfile? _editing;
    private bool _loading, _busy, _closed;
    private CancellationTokenSource? _request;
    private string? _deleteId;
    private DateTimeOffset _deleteUntil;
    private readonly Button _add, _delete, _refresh, _textTest, _visionTest, _cancel;
    internal ModelConfiguration? Baseline { get; private set; }
    internal Func<ModelServiceProfile, string?, CancellationToken, Task<IReadOnlyList<string>>>? Discover { get; set; }
    internal Func<ModelServiceProfile, string, string?, bool, CancellationToken, Task<ModelCatalogEntry>>? Verify { get; set; }
    private readonly bool _local;
    internal Task OperationTask { get; private set; } = Task.CompletedTask;
    internal IReadOnlyDictionary<string, string> PendingKeys => _pendingKeys;
    internal ComboBox ProfileSelector => _services;
    internal TextBox EndpointEditor => _endpoint;
    internal TextBox KeyEditor => _key;
    internal ComboBox ModelEditor => _model;
    internal string StatusMessage => _status.Text ?? "";

    public ConfigurationCenterView(bool local = false)
    {
        _local = local;
        _model.IsEditable = true;
        _key.PasswordChar = '●';
        _key.PlaceholderText = T("留空保留已保存密钥", "Leave blank to keep the saved key");
        _protocol.ItemsSource = ApiProtocolChoices.All;
        _preset.ItemsSource = local ? LocalLlmPresets.All.Cast<object>().ToArray() : ThirdPartyApiPresets.All.Cast<object>().ToArray();
        _add = Button("添加服务", "Add service", (_, _) => Sync(AddService));
        _delete = Button("删除服务", "Remove service", (_, _) => Sync(RemoveService));
        var apply = Button("填入预设", "Use preset", (_, _) => Sync(ApplyPreset));
        _refresh = Button("获取模型列表", "Fetch models", async (_, _) => { OperationTask = QueryAsync(null); await OperationTask; });
        _textTest = Button("测试文字能力", "Test text", async (_, _) => { OperationTask = QueryAsync(false); await OperationTask; });
        _visionTest = Button("测试图片能力", "Test vision", async (_, _) => { OperationTask = QueryAsync(true); await OperationTask; });
        _cancel = Button("取消请求", "Cancel request", (_, _) => _request?.Cancel());
        _cancel.IsVisible = false;
        Label(value => _clearKey.Content = value, "删除此服务已保存的密钥", "Delete the saved key for this service");
        _editor.Children.Add(Group("服务名称", "Service name", _name));
        _editor.Children.Add(Group("地址预设（不绑定账号）", "Endpoint preset (not an account)", _preset));
        _editor.Children.Add(apply);
        _editor.Children.Add(Group(local ? "本机服务地址" : "云端 API 地址", local ? "Local service URL" : "Cloud API URL", _endpoint));
        _editor.Children.Add(Group("接口协议", "API protocol", _protocol));
        _editor.Children.Add(Group("此服务的 API Key", "API key for this service", _key));
        _editor.Children.Add(_clearKey);
        _editor.Children.Add(Group("默认模型（可手动输入 ID）", "Default model (manual ID supported)", _model));
        _editor.Children.Add(new WrapPanel { Children = { _refresh, _textTest, _visionTest } });
        _editor.Children.Add(_capabilities);
        _editor.Children.Add(Text("获取列表不代表模型已验证。测试只发送固定文本或合成图片；远程服务可能计费，不会发送用户截图。", "A model list is not a capability test. Tests send fixed text or a synthetic image; remote charges may apply. No user screenshots are sent."));
        var servicePage = new StackPanel { Spacing = 12, Children =
        {
            _summary, _services, new WrapPanel { Children = { _add, _delete } }, _editor
        } };
        Content = new StackPanel { Spacing = 12, Children = { servicePage, _cancel, _status } };
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _services.SelectionChanged += (_, _) => SelectEditor();
        _endpoint.PropertyChanged += (_, e) => { if (!_loading && e.Property == TextBox.TextProperty) InvalidateIdentity(); };
        _protocol.SelectionChanged += (_, _) => { if (!_loading) InvalidateIdentity(); };
        _model.SelectionChanged += (_, _) => RefreshCapabilities();
        _model.PropertyChanged += (_, e) => { if (!_loading && e.Property == ComboBox.TextProperty) RefreshCapabilities(); };
        _clearKey.PropertyChanged += (_, e) => { if (!_loading && e.Property == CheckBox.IsCheckedProperty) RefreshCapabilities(); };
        _key.PropertyChanged += (_, e) => { if (!_loading && e.Property == TextBox.TextProperty) RefreshCapabilities(); };
        Load(new AppSettings());
    }

    internal void Load(AppSettings settings)
    {
        _loading = true;
        try
        {
            Baseline = settings.ModelConfiguration;
            _configuration = ConfigurationMigration.Upgrade(settings).ModelConfiguration!;
            _profiles.Clear(); _profiles.AddRange(_local ? _configuration.LocalServices : _configuration.Services);
            _pendingKeys.Clear(); _editing = null;
            _services.ItemsSource = _profiles.ToArray();
            _services.SelectedItem = _profiles.FirstOrDefault();
            LoadEditor(_profiles.FirstOrDefault());
        }
        finally { _loading = false; }
        RefreshLanguage();
        Status(T("测试文字和图片能力后，在“当前模型”中选择。修改后点击保存并应用。", "Test text and vision, then select this model under Current model. Save to apply edits."));
    }
    internal ModelConfiguration BuildDraft()
    {
        if (_busy) throw new ProviderException("profile_operation_active", "请先完成或取消服务请求，再保存配置。 / Finish or cancel the service request before saving.");
        SaveEditor();
        var draft = _local ? _configuration with { LocalServices = _profiles.ToArray() }
            : _configuration with { Services = _profiles.ToArray() };
        (draft with { CurrentModel = null }).Validate();
        return draft;
    }
    internal void AcceptSaved(AppSettings settings) { Load(settings); Status(T("配置与独立密钥已保存。", "Configuration and scoped credentials saved.")); }
    internal void Cancel()
    {
        _closed = true; _request?.Cancel(); _pendingKeys.Clear(); _key.Text = "";
    }
    internal void RefreshLanguage()
    {
        var loading = _loading; _loading = true;
        try
        {
            foreach (var label in _labels) label.Set(T(label.Zh, label.En));
            _summary.Text = T($"服务档案：{_profiles.Count} 个。编辑不会覆盖其他账号，点击底部保存后生效。", $"Service profiles: {_profiles.Count}. Other accounts stay separate; edits take effect after saving below.");
            _key.PlaceholderText = T("留空保留已保存密钥", "Leave blank to keep the saved key");
            AutomationProperties.SetName(_services, T("选择服务档案", "Select service profile"));
        }
        finally { _loading = loading; }
        RefreshCapabilities();
    }

    private void SelectEditor()
    {
        if (_loading) return;
        var next = _services.SelectedItem as ModelServiceProfile;
        try { SaveEditor(); LoadEditor(next is null ? null : _profiles.First(p => p.Id == next.Id)); }
        catch (Exception e)
        {
            _loading = true; _services.SelectedItem = _editing; _loading = false;
            Status(SafeError(e));
        }
    }
    private void LoadEditor(ModelServiceProfile? profile)
    {
        var loading = _loading; _loading = true;
        try
        {
            _editing = profile; _editor.IsVisible = profile is not null;
            _name.Text = profile?.Name ?? ""; _endpoint.Text = profile?.Endpoint ?? "";
            _protocol.SelectedItem = ApiProtocolChoices.Get(profile?.Protocol);
            _model.ItemsSource = profile?.Models.Select(m => m.Id).ToArray() ?? [];
            _model.Text = profile?.DefaultModel ?? "";
            _key.Text = ""; _clearKey.IsChecked = false;
            _preset.SelectedItem = null;
        }
        finally { _loading = loading; }
        RefreshCapabilities();
    }
    private void InvalidateIdentity()
    {
        if (_editing is null) return;
        _key.Text = ""; _clearKey.IsChecked = false;
        foreach (var key in _pendingKeys.Keys.Where(k => k.StartsWith("model-service-" + _editing.Id + "-", StringComparison.Ordinal)).ToArray()) _pendingKeys.Remove(key);
        _model.ItemsSource = Array.Empty<string>(); _model.Text = "";
        Status(T("服务地址或协议改变：请重新填写此服务密钥并获取模型，原账号不会被发送到新地址。", "URL or protocol changed: enter this service's key and fetch models again. The old account key will not be sent to the new URL."));
    }
    private void SaveEditor()
    {
        if (_editing is null) return;
        var endpoint = ModelServiceProfile.CanonicalEndpoint(_endpoint.Text ?? "");
        var protocol = (_protocol.SelectedItem as ApiProtocolChoice)?.Id ?? ModelApiProtocols.ChatCompletions;
        var identityChanged = endpoint != _editing.Endpoint || protocol != _editing.Protocol;
        var keyChanged = _clearKey.IsChecked == true || !string.IsNullOrWhiteSpace(_key.Text);
        var catalog = identityChanged ? [] : keyChanged
            ? _editing.Models.Select(m => m with { TextVerified = false, VisionVerified = false }).ToArray()
            : _editing.Models;
        var updated = _editing with { Name = _name.Text?.Trim() ?? "", Endpoint = endpoint,
            Protocol = protocol, DefaultModel = _model.Text?.Trim() ?? "", Models = catalog,
            Backend = _local ? LocalLlmPresets.MatchEndpoint(endpoint)?.Id ?? "openai-compatible" : "openai-compatible" };
        updated.Validate();
        if (updated.IsRemote == _local)
            throw new ProviderException("service_location_invalid", T("请在云端 API 页管理远程地址，在本地模型页管理回环地址。", "Use Cloud APIs for remote URLs and Local models for loopback URLs."));
        if (_clearKey.IsChecked == true) _pendingKeys[updated.CredentialRef] = "";
        else if (!string.IsNullOrWhiteSpace(_key.Text)) _pendingKeys[updated.CredentialRef] = _key.Text.Trim();
        _key.Text = ""; _clearKey.IsChecked = false;
        var index = _profiles.FindIndex(p => p.Id == updated.Id);
        if (index >= 0) _profiles[index] = updated;
        _editing = updated;
        // Refresh renamed choices without losing either the target service selection or unsaved task overrides.
        var selectedId = (_services.SelectedItem as ModelServiceProfile)?.Id;
        var loading = _loading; _loading = true;
        try
        {
            _services.ItemsSource = _profiles.ToArray();
            _services.SelectedItem = _profiles.FirstOrDefault(p => p.Id == selectedId);
        }
        finally { _loading = loading; }
        RefreshCapabilities();
    }
    private void AddService()
    {
        SaveEditor();
        if (_profiles.Count >= 32) throw new ProviderException("profile_limit", "服务数量上限为 32。 / At most 32 services are supported.");
        var profile = new ModelServiceProfile { Name = T("新服务", "New service") + " " + (_profiles.Count + 1),
            Endpoint = _local ? AppSettings.DefaultCustomTranslationEndpoint : "https://api.openai.com/v1/chat/completions",
            Protocol = _local ? ModelApiProtocols.ChatCompletions : ModelApiProtocols.Responses };
        _profiles.Add(profile); ResetServiceChoices(profile.Id);
    }
    private void RemoveService()
    {
        if (_editing is null) return;
        if (_configuration.CurrentModel?.ServiceId == _editing.Id)
            throw new ProviderException("profile_in_use", "请先保存另一个当前模型（或清空选择），再删除此服务。 / Save another current model (or clear the selection) before removing this service.");
        if (_deleteId != _editing.Id || DateTimeOffset.UtcNow > _deleteUntil)
        {
            _deleteId = _editing.Id; _deleteUntil = DateTimeOffset.UtcNow.AddSeconds(10);
            Status(T("10 秒内再次点击删除确认。保存后清理此服务独立密钥，不删除模型文件或其他账号。", "Click Remove again within 10 seconds. Saving removes this service's scoped key, not model files or other accounts.")); return;
        }
        _profiles.RemoveAll(p => p.Id == _editing.Id);
        foreach (var key in _pendingKeys.Keys.Where(k => k.StartsWith("model-service-" + _editing.Id + "-", StringComparison.Ordinal)).ToArray()) _pendingKeys.Remove(key);
        _editing = null; _deleteId = null; ResetServiceChoices(_profiles.FirstOrDefault()?.Id);
    }
    private void ApplyPreset()
    {
        if (_preset.SelectedItem is ThirdPartyApiPreset remote)
        { _endpoint.Text = remote.ChatCompletionsEndpoint; _protocol.SelectedItem = ApiProtocolChoices.Get(remote.DefaultProtocol); }
        else if (_preset.SelectedItem is LocalLlmPreset local)
        { _endpoint.Text = local.ChatCompletionsEndpoint; _protocol.SelectedItem = ApiProtocolChoices.Get(ModelApiProtocols.ChatCompletions); }
    }
    private void ResetServiceChoices(string? id)
    {
        _loading = true;
        try
        {
            _services.ItemsSource = _profiles.ToArray();
            _services.SelectedItem = _profiles.FirstOrDefault(p => p.Id == id);
            LoadEditor(_services.SelectedItem as ModelServiceProfile);
        }
        finally { _loading = false; }
        RefreshLanguage();
    }
    private ModelServiceProfile? CurrentEditorProfile()
    {
        if (_editing is null) return null;
        var endpoint = _endpoint.Text ?? "";
        var protocol = (_protocol.SelectedItem as ApiProtocolChoice)?.Id ?? ModelApiProtocols.ChatCompletions;
        var sameIdentity = false;
        try
        {
            endpoint = ModelServiceProfile.CanonicalEndpoint(endpoint);
            sameIdentity = endpoint == ModelServiceProfile.CanonicalEndpoint(_editing.Endpoint) && protocol == _editing.Protocol;
        }
        catch (ProviderException) { }
        var models = sameIdentity ? _editing.Models : [];
        if (!string.IsNullOrWhiteSpace(_key.Text) || _clearKey.IsChecked == true)
            models = models.Select(m => m with { TextVerified = false, VisionVerified = false }).ToArray();
        return _editing with { Name = _name.Text?.Trim() ?? "", Endpoint = endpoint, Protocol = protocol,
            DefaultModel = _model.Text?.Trim() ?? "", Models = models };
    }

    internal string CapabilityText => _capabilities.Text ?? "";

    private void RefreshCapabilities()
    {
        var entry = CurrentEditorProfile()?.Models.FirstOrDefault(m => m.Id == _model.Text);
        _capabilities.Text = T("文字能力：", "Text: ") + (entry?.TextVerified == true ? T("已测试", "tested") : T("未验证", "unverified")) +
            "  ·  " + T("图片能力：", "Vision: ") + (entry?.VisionVerified == true ? T("已测试", "tested") : T("未验证", "unverified"));
    }
    private async Task QueryAsync(bool? vision)
    {
        if (_busy || _closed) return;
        try
        {
            SaveEditor();
            if (_editing is not { } profile) return;
            _pendingKeys.TryGetValue(profile.CredentialRef, out var draftKey);
            if (vision is not null)
            {
                profile = profile with { Models = profile.Models.Select(m => m.Id != profile.DefaultModel ? m :
                    vision.Value ? m with { VisionVerified = false } : m with { TextVerified = false }).ToArray() };
                _profiles[_profiles.FindIndex(p => p.Id == profile.Id)] = profile;
                _editing = profile;
                RefreshCapabilities();
            }
            _request = new CancellationTokenSource(); SetBusy(true);
            Status(T("正在请求所选服务；配置尚未保存。", "Requesting the selected service; configuration is not saved yet."));
            if (vision is null)
            {
                if (Discover is null) throw new InvalidOperationException();
                var ids = await Discover(profile, draftKey, _request.Token);
                _request.Token.ThrowIfCancellationRequested();
                if (_closed) return;
                profile = profile with { Models = ids.Union(profile.Models.Select(m => m.Id)).Select(id => profile.Models.FirstOrDefault(m => m.Id == id) ?? new ModelCatalogEntry(id)).ToArray() };
                Status(T($"API 返回 {ids.Count} 个模型；列表可见不代表此账号一定能推理，也不代表支持图片。", $"API returned {ids.Count} models. Listing does not guarantee inference access or vision support."));
            }
            else
            {
                if (Verify is null) throw new InvalidOperationException();
                var entry = await Verify(profile, profile.DefaultModel, draftKey, vision.Value, _request.Token);
                _request.Token.ThrowIfCancellationRequested();
                if (_closed) return;
                profile = profile with { Models = profile.Models.Where(m => m.Id != entry.Id).Append(entry).ToArray() };
                Status(T("固定样例测试通过。请保存以记录该模型能力；这不等于所有任务的质量保证。", "Synthetic test passed. Save to record this model capability; this is not a quality guarantee for every task."));
            }
            _profiles[_profiles.FindIndex(p => p.Id == profile.Id)] = profile;
            ResetServiceChoices(profile.Id);
        }
        catch (OperationCanceledException) { if (!_closed) Status(T("请求已取消或超时，可手动填写模型 ID。", "Request cancelled or timed out; manual model IDs remain available.")); }
        catch (Exception e) { if (!_closed) Status(SafeError(e)); }
        finally
        {
            _request?.Dispose(); _request = null; SetBusy(false);
            if (!_closed) { RefreshCapabilities(); }
        }
    }
    private void SetBusy(bool busy)
    {
        _busy = busy; _editor.IsEnabled = _services.IsEnabled = _add.IsEnabled = _delete.IsEnabled = !busy;
        _cancel.IsVisible = busy;
    }
    private void Sync(Action action) { if (_busy) return; try { action(); } catch (Exception e) { Status(SafeError(e)); } }
    private static string SafeError(Exception error) => error is ProviderException authored
        ? UiText.Error(authored)
        : T("操作未完成，请检查服务地址、密钥、网络和协议；可手动输入模型 ID。未显示原始错误内容。", "Operation failed. Check the service URL, key, network and protocol; manual model IDs remain available. Raw error details are not displayed.");
    private void Status(string text) => _status.Text = text;
    private static string T(string zh, string en) => UiText.IsEnglish ? en : zh;
    private void Label(Action<string> set, string zh, string en) { _labels.Add((set, zh, en)); set(T(zh, en)); }
    private TextBlock Text(string zh, string en, bool heading = false)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = heading ? FontWeight.SemiBold : FontWeight.Normal };
        Label(value => text.Text = value, zh, en); return text;
    }
    private StackPanel Group(string zh, string en, Control control)
    {
        Label(value => AutomationProperties.SetName(control, value), zh, en);
        return new() { Spacing = 5, Children = { Text(zh, en, true), control } };
    }
    private Button Button(string zh, string en, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button { Margin = new Thickness(0, 0, 8, 6), HorizontalAlignment = HorizontalAlignment.Left };
        button.Classes.Add("secondary");
        Label(value => { button.Content = value; AutomationProperties.SetName(button, value); }, zh, en);
        button.Click += click; return button;
    }
    private static TControl Field<TControl>(string name) where TControl : Control, new()
    {
        var field = new TControl { Name = name, HorizontalAlignment = HorizontalAlignment.Stretch };
        field.Classes.Add("workspace-field"); AutomationProperties.SetName(field, name); return field;
    }
}

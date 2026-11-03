using Avalonia.Controls;
using Avalonia.Interactivity;
using PingYi.Core;

namespace PingYi.App;

public partial class SettingsWindow
{
    private sealed record ConfigurationChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record ConfigurationServiceChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record ConfigurationRouteChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private string? _editingConfigurationServiceId;
    private bool _isLoadingConfiguration;

    private void RefreshPrivacySettingsLanguage()
    {
        var en = UiText.IsEnglish;
        PrivacySettingsNavText.Text = en ? "Privacy & security" : "隐私与安全";
        PrivacySettingsTitleText.Text = en ? "Privacy & security" : "隐私与安全";
        PrivacySettingsHintText.Text = en
            ? "Choose whether PingYi must ask before sending recognized text or selected screenshots to remote model providers."
            : "设置是否在将识别文字或所选截图发送给远程模型服务商前逐次征求确认。";
        ConfirmRemoteRequestsCheckBox.Content = en
            ? "Ask before every remote model request"
            : "每次远程模型请求前都确认";
        RemoteTextDataBoundaryText.Text = en
            ? "Text translation sends the recognized text and task instructions to the selected provider. The provider may log or retain requests and may charge for usage."
            : "文字翻译会将识别文字和任务指令发送给所选服务商。服务商可能记录或保留请求，并可能按用量收费。";
        RemoteImageDataBoundaryText.Text = en
            ? "Remote OCR, image description and prompt reconstruction send the selected screenshot to the provider. QR decoding remains local and is never routed to a model."
            : "远程 OCR、图片描述和提示词反推会将所选截图发送给服务商。二维码解析始终在本地执行，不会路由到大模型。";
        SecretStoreTitleText.Text = en ? "Credential storage" : "密钥存储";
        SecretStoreBoundaryText.Text = en
            ? "Service API keys are stored by credential reference in the operating system secure store. settings.json contains references only, never the key values."
            : "各服务 API Key 按独立 credentialRef 保存在操作系统安全存储中。settings.json 只保存引用，不保存密钥值。";
    }

    private void RefreshSecretStoreStatus()
    {
        if (_services is null)
        {
            SecretStoreStatusText.Text = UiText.IsEnglish ? "Secure store status is available after startup." : "启动后可检查系统安全存储状态。";
            return;
        }
        var persistent = _services.SecretStore is not PlatformSecretStore store || store.IsPersistent;
        SecretStoreStatusText.Text = persistent
            ? (UiText.IsEnglish ? "Credential storage: persistent OS secure store." : "密钥存储：使用持久化系统安全存储。")
            : (UiText.IsEnglish ? "Warning: OS secure storage is unavailable; keys remain in memory only for this session." : "警告：系统安全存储不可用，密钥仅保存在本次运行的内存中。");
        ThemeResources.Use(SecretStoreStatusText, TextBlock.ForegroundProperty,
            persistent ? "SuccessTextBrush" : "WarningTextBrush");
    }

    private void InitializeConfigurationCenter()
    {
        ConfigServiceProtocolCombo.ItemsSource = ApiProtocolChoices.All;
        ConfigServicePresetCombo.ItemsSource = ThirdPartyApiPresets.All;
        RefreshConfigurationCenterLanguage();
        RefreshPrivacySettingsLanguage();
    }

    private void RefreshConfigurationCenterLanguage()
    {
        var en = UiText.IsEnglish;
        var modes = new[]
        {
            new ConfigurationChoice(ConfigurationModes.Hybrid, en ? "Hybrid · per-task routing" : "混合模式 · 任务独立路由"),
            new ConfigurationChoice(ConfigurationModes.LocalFirst, en ? "Local first" : "本地优先"),
            new ConfigurationChoice(ConfigurationModes.CloudFirst, en ? "Cloud first" : "云端优先")
        };
        var selectedMode = (ConfigurationModeCombo.SelectedItem as ConfigurationChoice)?.Id;
        ConfigurationModeCombo.ItemsSource = modes;
        ConfigurationModeCombo.SelectedItem = modes.FirstOrDefault(item => item.Id == selectedMode) ?? modes[0];
        var kinds = new[]
        {
            new ConfigurationChoice(ModelServiceKinds.Local, en ? "Local service" : "本地服务"),
            new ConfigurationChoice(ModelServiceKinds.Remote, en ? "Remote API" : "远程 API")
        };
        var selectedKind = (ConfigServiceKindCombo.SelectedItem as ConfigurationChoice)?.Id;
        ConfigServiceKindCombo.ItemsSource = kinds;
        ConfigServiceKindCombo.SelectedItem = kinds.FirstOrDefault(item => item.Id == selectedKind) ?? kinds[1];
        ConfigurationCenterNavText.Text = en ? "Configuration" : "配置中心";
        ConfigurationCenterTitleText.Text = en ? "Configuration center" : "配置中心";
        ConfigurationCenterHintText.Text = en
            ? "Manage independent model services and assign a different service to each task. API keys are stored separately in the OS secure store."
            : "独立管理多个模型服务，并为每种任务分配不同服务。每个服务的 API Key 单独保存在系统安全存储中。";
        ConfigurationModeLabelText.Text = en ? "Execution mode" : "运行模式";
        ConfigurationModeHintText.Text = en
            ? "Hybrid mode lets each task use a different local or cloud service. Local-first and Cloud-first are routing preferences, not permission to upload data silently."
            : "混合模式允许每种任务分别使用本地或云端服务。本地优先和云端优先只是路由偏好，不代表允许静默上传数据。";
        ConfigurationServicesTitleText.Text = en ? "Model services" : "模型服务";
        ApplyConfigServicePresetButton.Content = en ? "Apply preset" : "填入预设";
        AddConfigurationServiceButton.Content = en ? "Add service" : "添加服务";
        DeleteConfigurationServiceButton.Content = en ? "Delete service" : "删除服务";
        ConfigServiceNameLabelText.Text = en ? "Service name" : "服务名称";
        ConfigServiceKindLabelText.Text = en ? "Service type" : "服务类型";
        ConfigServiceEndpointLabelText.Text = en ? "Chat endpoint" : "服务地址";
        ConfigServiceProtocolLabelText.Text = en ? "API protocol" : "API 协议";
        ConfigServiceModelLabelText.Text = en ? "Default model ID" : "默认模型 ID";
        ConfigServiceKeyLabelText.Text = "API Key";
        ConfigServiceTextCapabilityCheckBox.Content = en ? "Text generation" : "文字生成";
        ConfigServiceImageCapabilityCheckBox.Content = en ? "Image input" : "图片输入";
        ConfigServiceEnabledCheckBox.Content = en ? "Enabled" : "启用服务";
        ConfigServiceCredentialHintText.Text = en
            ? "Leave API Key empty to keep the existing key. Each service has its own credential reference; keys are never written to settings.json."
            : "API Key 留空表示保留原密钥。每个服务拥有独立密钥引用，密钥不会写入 settings.json。";
        SaveConfigurationServiceButton.Content = en ? "Save service" : "保存服务";
        TestConfigurationServiceButton.Content = en ? "Test / list models" : "测试并获取模型";
        ConfigurationTasksTitleText.Text = en ? "Task routing" : "任务路由";
        ConfigurationTasksHintText.Text = en
            ? "Assign a service to each task. OCR and vision tasks should use a model declared with image input. QR decoding always stays local."
            : "为每项任务单独分配服务。OCR 和视觉任务应选择已声明支持图片输入的模型。二维码解析始终在本地执行。";
        ConfigOcrRouteLabelText.Text = en ? "Text OCR" : "文字 OCR";
        ConfigTranslationRouteLabelText.Text = en ? "Translation" : "文字翻译";
        ConfigImageDescriptionRouteLabelText.Text = en ? "Image description" : "图片描述";
        ConfigPromptReconstructionRouteLabelText.Text = en ? "Prompt reconstruction" : "提示词反推";
        ConfigQrRouteLabelText.Text = en ? "QR decoding" : "二维码解析";
        ConfigQrRouteText.Text = en ? "Built-in ZXing · local only" : "内置 ZXing · 仅本地解析";
        ConfigOcrFallbackCheckBox.Content = en ? "Fall back to local PaddleOCR if OCR is unavailable" : "OCR 不可用时回退本地 PaddleOCR";
        ConfigTranslationFallbackCheckBox.Content = en ? "Fall back to local Argos if translation is unavailable" : "翻译不可用时回退本地 Argos";
        SaveTaskRoutesButton.Content = en ? "Save task routing" : "保存任务路由";
    }

    private void RefreshConfigurationCenter()
    {
        if (_services is null) return;
        _isLoadingConfiguration = true;
        try
        {
            var config = _services.Settings.Configuration;
            ConfigurationModeCombo.SelectedItem = ConfigurationModeCombo.ItemsSource is IEnumerable<ConfigurationChoice> modes
                ? modes.FirstOrDefault(item => item.Id == config.Mode) : null;
            var profiles = config.Services;
            var profileChoices = profiles.Select(profile => new ConfigurationRouteChoice(
                profile.Id, $"{profile.Name} · {profile.DefaultModelId}" +
                (profile.Capabilities.Contains("image-input", StringComparer.OrdinalIgnoreCase) ? " · Vision" : ""))).ToArray();
            ConfigurationServiceCombo.ItemsSource = profiles.Select(profile =>
                new ConfigurationServiceChoice(profile.Id, $"{profile.Name} · {profile.Kind}")).ToArray();
            var ocrChoices = profileChoices.Concat(_services.Providers.OcrProviders
                .Where(provider => provider.Metadata.Id != "local-vlm-ocr")
                .Select(provider => new ConfigurationRouteChoice("provider:" + provider.Metadata.Id, provider.Metadata.DisplayName))).ToArray();
            var translationChoices = profileChoices.Concat(_services.Providers.TranslationProviders
                .Where(provider => provider.Metadata.Id != "custom-chat")
                .Select(provider => new ConfigurationRouteChoice("provider:" + provider.Metadata.Id, provider.Metadata.DisplayName))).ToArray();
            ConfigOcrRouteCombo.ItemsSource = ocrChoices;
            ConfigTranslationRouteCombo.ItemsSource = translationChoices;
            ConfigImageDescriptionRouteCombo.ItemsSource = profileChoices;
            ConfigPromptReconstructionRouteCombo.ItemsSource = profileChoices;
            ConfigOcrRouteCombo.SelectedItem = ocrChoices.FirstOrDefault(choice => choice.Id == config.Tasks.Ocr.ServiceId);
            ConfigTranslationRouteCombo.SelectedItem = translationChoices.FirstOrDefault(choice => choice.Id == config.Tasks.Translation.ServiceId);
            ConfigImageDescriptionRouteCombo.SelectedItem = profileChoices.FirstOrDefault(choice => choice.Id == config.Tasks.ImageDescription.ServiceId);
            ConfigPromptReconstructionRouteCombo.SelectedItem = profileChoices.FirstOrDefault(choice => choice.Id == config.Tasks.PromptReconstruction.ServiceId);
            ConfigOcrFallbackCheckBox.IsChecked = config.Tasks.Ocr.AllowFallback;
            ConfigTranslationFallbackCheckBox.IsChecked = config.Tasks.Translation.AllowFallback;
            ConfirmRemoteRequestsCheckBox.IsChecked = _services.Settings.ConfirmRemoteRequests;
            RefreshSecretStoreStatus();

            if (_editingConfigurationServiceId is null || !profiles.Any(profile => profile.Id == _editingConfigurationServiceId))
                _editingConfigurationServiceId = profiles.FirstOrDefault()?.Id;
            var selected = profiles.FirstOrDefault(profile => profile.Id == _editingConfigurationServiceId);
            ConfigurationServiceCombo.SelectedItem = selected is null ? null :
                ConfigurationServiceCombo.ItemsSource is IEnumerable<ConfigurationServiceChoice> choices
                    ? choices.FirstOrDefault(choice => choice.Id == selected.Id) : null;
            LoadConfigurationService(selected);
            ConfigurationServiceStatusText.Text = string.Empty;
            ConfigurationRoutesStatusText.Text = string.Empty;
        }
        finally { _isLoadingConfiguration = false; }
    }

    private void LoadConfigurationService(ModelServiceProfile? profile)
    {
        _isLoadingConfiguration = true;
        try
        {
            ConfigServiceNameBox.Text = profile?.Name ?? string.Empty;
            ConfigServiceEndpointBox.Text = profile?.Endpoint ?? string.Empty;
            var knownModels = _services?.Settings.Configuration.Models
                .Where(model => model.ServiceId == profile?.Id).Select(model => model.ModelId).ToArray() ?? [];
            ConfigServiceModelCombo.ItemsSource = knownModels;
            ConfigServiceModelCombo.Text = profile?.DefaultModelId ?? string.Empty;
            ConfigServiceProtocolCombo.SelectedItem = ApiProtocolChoices.Get(profile?.Protocol);
            ConfigServiceKindCombo.SelectedItem = ConfigServiceKindCombo.ItemsSource is IEnumerable<ConfigurationChoice> kinds
                ? kinds.FirstOrDefault(item => item.Id == (profile?.Kind ?? ModelServiceKinds.Remote)) : null;
            ConfigServiceTextCapabilityCheckBox.IsChecked = profile?.Capabilities.Contains("text-generation", StringComparer.OrdinalIgnoreCase) ?? true;
            ConfigServiceImageCapabilityCheckBox.IsChecked = profile?.Capabilities.Contains("image-input", StringComparer.OrdinalIgnoreCase) ?? false;
            ConfigServiceEnabledCheckBox.IsChecked = profile?.Enabled ?? true;
            ConfigServiceApiKeyBox.Text = string.Empty;
            DeleteConfigurationServiceButton.IsEnabled = profile is not null && profile.Id != "legacy-custom";
        }
        finally { _isLoadingConfiguration = false; }
    }

    private void ConfigurationServiceCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingConfiguration || _services is null ||
            ConfigurationServiceCombo.SelectedItem is not ConfigurationServiceChoice choice) return;
        _editingConfigurationServiceId = choice.Id;
        LoadConfigurationService(_services.Settings.Configuration.Services.FirstOrDefault(profile => profile.Id == choice.Id));
    }

    private void ApplyConfigServicePresetButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ConfigServicePresetCombo.SelectedItem is not ThirdPartyApiPreset preset) return;
        ConfigServiceNameBox.Text = preset.DisplayName;
        ConfigServiceEndpointBox.Text = preset.ChatCompletionsEndpoint;
        ConfigServiceProtocolCombo.SelectedItem = ApiProtocolChoices.Get(preset.DefaultProtocol);
        ConfigServiceKindCombo.SelectedItem = ConfigServiceKindCombo.ItemsSource is IEnumerable<ConfigurationChoice> kinds
            ? kinds.FirstOrDefault(item => item.Id == ModelServiceKinds.Remote) : null;
        SetInlineStatus(ConfigurationServiceStatusText,
            UiText.IsEnglish ? "Provider endpoint filled in. Enter the exact model ID and API Key, then save." : "已填入服务商地址。请填写准确模型 ID 和 API Key，然后保存。",
            "SecondaryTextBrush");
    }

    private void AddConfigurationServiceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _editingConfigurationServiceId = null;
        ConfigurationServiceCombo.SelectedItem = null;
        LoadConfigurationService(null);
        ConfigServiceNameBox.Text = UiText.IsEnglish ? "New model service" : "新模型服务";
        ConfigServiceKindCombo.SelectedItem = ConfigServiceKindCombo.ItemsSource is IEnumerable<ConfigurationChoice> kinds
            ? kinds.FirstOrDefault(item => item.Id == ModelServiceKinds.Remote) : null;
        ConfigServiceEndpointBox.Text = "https://";
        ConfigServiceProtocolCombo.SelectedItem = ApiProtocolChoices.Get(ModelApiProtocols.ChatCompletions);
        ConfigurationServiceStatusText.Text = UiText.IsEnglish
            ? "Enter a provider endpoint, model ID and optional API Key, then save."
            : "填写服务商地址、模型 ID 和可选 API Key，然后保存。";
    }

    private async void SaveConfigurationServiceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        var button = sender as Button;
        BeginButtonOperation(button, UiText.IsEnglish ? "Saving…" : "正在保存…");
        try
        {
            var endpointText = ConfigServiceEndpointBox.Text?.Trim() ?? string.Empty;
            if (!AppSettings.TryParseChatCompletionsEndpoint(endpointText, out var endpoint))
                throw new ProviderException("configuration_service_endpoint", "请填写有效的 Chat Completions 地址。");
            if (!AppSettings.IsChatCompletionsTransportAllowed(endpoint))
                throw new ProviderException("configuration_service_transport", "远程服务必须使用 HTTPS；仅本机回环地址允许 HTTP。");

            var current = _services.Settings;
            var existing = current.Configuration.Services.FirstOrDefault(profile => profile.Id == _editingConfigurationServiceId);
            var id = existing?.Id ?? "service-" + Guid.NewGuid().ToString("N")[..10];
            var credentialRef = existing?.CredentialRef ?? ServiceCredentialRefs.ForService(id);
            var capabilities = new List<string>();
            if (ConfigServiceTextCapabilityCheckBox.IsChecked == true) capabilities.Add("text-generation");
            if (ConfigServiceImageCapabilityCheckBox.IsChecked == true) capabilities.Add("image-input");
            if (capabilities.Count == 0) throw new ProviderException("configuration_service_capability", "至少选择一项模型能力。");

            var profile = new ModelServiceProfile
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(ConfigServiceNameBox.Text) ? id : ConfigServiceNameBox.Text.Trim(),
                Kind = endpoint.IsLoopback ? ModelServiceKinds.Local : ModelServiceKinds.Remote,
                Protocol = (ConfigServiceProtocolCombo.SelectedItem as ApiProtocolChoice)?.Id ?? ModelApiProtocols.ChatCompletions,
                Endpoint = AppSettings.NormalizeChatCompletionsEndpoint(endpointText),
                CredentialRef = credentialRef,
                DefaultModelId = ConfigServiceModelCombo.Text?.Trim() ?? string.Empty,
                Enabled = ConfigServiceEnabledCheckBox.IsChecked == true,
                Capabilities = capabilities
            };
            var services = current.Configuration.Services.Where(item => item.Id != id).Append(profile).ToArray();
            var models = current.Configuration.Models
                .Where(model => model.ServiceId != id || model.ModelId != profile.DefaultModelId)
                .ToList();
            if (!string.IsNullOrWhiteSpace(profile.DefaultModelId))
                models.Add(new ModelCatalogEntry
                {
                    ServiceId = id,
                    ModelId = profile.DefaultModelId,
                    Capabilities = profile.Capabilities,
                    DiscoverySource = "manual"
                });
            var updated = current with
            {
                Configuration = current.Configuration with { Services = services, Models = models }
            };
            if (id == "legacy-custom")
                updated = updated with
                {
                    CustomTranslationEndpoint = profile.Endpoint,
                    CustomTranslationModel = profile.DefaultModelId,
                    CustomTranslationApiProtocol = profile.Protocol
                };
            var key = ConfigServiceApiKeyBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(key))
                await _services.SecretStore.SetAsync(profile.CredentialRef, key);
            await _services.SaveSettingsAsync(updated);
            _editingConfigurationServiceId = id;
            ConfigServiceApiKeyBox.Text = string.Empty;
            RefreshConfigurationCenter();
            SetInlineStatus(ConfigurationServiceStatusText,
                UiText.IsEnglish ? "Service saved. API Key is stored separately in the OS secure store." : "服务已保存，API Key 已独立写入系统安全存储。",
                "SuccessTextBrush");
            SetGlobalStatus(UiText.IsEnglish ? "Model service profile saved." : "模型服务配置已保存。", false);
            FinishButtonOperation(button, UiText.IsEnglish ? "Saved" : "已保存", success: true);
        }
        catch (Exception exception)
        {
            SetInlineStatus(ConfigurationServiceStatusText, UiText.Error(exception), "DangerTextBrush");
            SetGlobalStatus(UiText.Error(exception), true);
            FinishButtonOperation(button, UiText.IsEnglish ? "Save failed" : "保存失败", success: false);
        }
    }

    private async void TestConfigurationServiceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null || string.IsNullOrWhiteSpace(_editingConfigurationServiceId)) return;
        var button = sender as Button;
        BeginButtonOperation(button, UiText.IsEnglish ? "Testing…" : "正在测试…");
        try
        {
            var key = ConfigServiceApiKeyBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                await _services.SaveServiceCredentialAsync(_editingConfigurationServiceId, key);
                ConfigServiceApiKeyBox.Text = string.Empty;
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var models = await _services.TestServiceProfileAsync(_editingConfigurationServiceId, timeout.Token);
            ConfigServiceModelCombo.ItemsSource = models;
            if (string.IsNullOrWhiteSpace(ConfigServiceModelCombo.Text) && models.Count > 0)
                ConfigServiceModelCombo.SelectedItem = models[0];
            SetInlineStatus(ConfigurationServiceStatusText,
                UiText.IsEnglish ? $"Connected. {models.Count} models returned." : $"连接成功，服务返回 {models.Count} 个模型。",
                "SuccessTextBrush");
            FinishButtonOperation(button, UiText.IsEnglish ? "Connected" : "连接成功", success: true);
        }
        catch (Exception exception)
        {
            SetInlineStatus(ConfigurationServiceStatusText, UiText.Error(exception), "DangerTextBrush");
            FinishButtonOperation(button, UiText.IsEnglish ? "Test failed" : "测试失败", success: false);
        }
    }

    private async void DeleteConfigurationServiceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null || string.IsNullOrWhiteSpace(_editingConfigurationServiceId)) return;
        var id = _editingConfigurationServiceId;
        if (id == "legacy-custom")
        {
            SetGlobalStatus(UiText.IsEnglish ? "The migrated compatibility service cannot be deleted; edit or disable it instead." : "迁移的兼容服务不能删除，可以编辑或停用。", true);
            return;
        }
        var tasks = _services.Settings.Configuration.Tasks;
        if (new[] { tasks.Ocr, tasks.Translation, tasks.ImageDescription, tasks.PromptReconstruction }
            .Any(route => route.ServiceId == id))
        {
            SetGlobalStatus(UiText.IsEnglish ? "This service is assigned to a task. Reassign the task before deleting it." : "该服务仍被任务使用，请先重新分配任务再删除。", true);
            return;
        }
        try
        {
            var current = _services.Settings;
            var updated = current with
            {
                Configuration = current.Configuration with
                {
                    Services = current.Configuration.Services.Where(item => item.Id != id).ToArray()
                }
            };
            await _services.DeleteServiceCredentialAsync(id);
            await _services.SaveSettingsAsync(updated);
            _editingConfigurationServiceId = null;
            RefreshConfigurationCenter();
            SetGlobalStatus(UiText.IsEnglish ? "Service deleted." : "服务已删除。", false);
        }
        catch (Exception exception) { SetGlobalStatus(UiText.Error(exception), true); }
    }

    private async void SaveTaskRoutesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        try
        {
            var current = _services.Settings;
            var tasks = current.Configuration.Tasks;
            TaskModelRoute BuildRoute(ComboBox combo, TaskModelRoute previous, bool imageInput, string fallback)
            {
                if (combo.SelectedItem is not ConfigurationRouteChoice choice)
                    throw new ProviderException("configuration_route_missing", "请为每项任务选择服务。");
                var profile = current.Configuration.Services.FirstOrDefault(item => item.Id == choice.Id);
                var builtIn = choice.Id.StartsWith("provider:", StringComparison.Ordinal);
                if (!builtIn && profile is null)
                    throw new ProviderException("configuration_route_missing", "所选服务已不存在。");
                if (imageInput && !builtIn &&
                    !profile!.Capabilities.Contains("image-input", StringComparer.OrdinalIgnoreCase))
                    throw new ProviderException("configuration_route_capability", $"服务“{profile!.Name}”未声明支持图片输入，不能用于该视觉任务。");
                return new TaskModelRoute
                {
                    ServiceId = choice.Id,
                    ModelId = profile?.DefaultModelId ?? string.Empty,
                    FallbackServiceId = previous.FallbackServiceId ?? fallback,
                    FallbackModelId = previous.FallbackModelId,
                    AllowFallback = previous.AllowFallback,
                    RequireImageInput = imageInput
                };
            }

            var updatedTasks = tasks with
            {
                Ocr = BuildRoute(ConfigOcrRouteCombo, tasks.Ocr, imageInput: true, "provider:local-paddle") with
                {
                    AllowFallback = ConfigOcrFallbackCheckBox.IsChecked == true,
                    FallbackServiceId = "provider:local-paddle"
                },
                Translation = BuildRoute(ConfigTranslationRouteCombo, tasks.Translation, imageInput: false, "provider:local-argos") with
                {
                    AllowFallback = ConfigTranslationFallbackCheckBox.IsChecked == true,
                    FallbackServiceId = "provider:local-argos"
                },
                ImageDescription = BuildRoute(ConfigImageDescriptionRouteCombo, tasks.ImageDescription, imageInput: true, string.Empty),
                PromptReconstruction = BuildRoute(ConfigPromptReconstructionRouteCombo, tasks.PromptReconstruction, imageInput: true, string.Empty),
                QrCode = new TaskModelRoute { ServiceId = "builtin:qr", ModelId = "zxing" }
            };
            var mode = (ConfigurationModeCombo.SelectedItem as ConfigurationChoice)?.Id ?? ConfigurationModes.Hybrid;
            var ocrLegacyId = updatedTasks.Ocr.ServiceId.StartsWith("provider:", StringComparison.Ordinal)
                ? updatedTasks.Ocr.ServiceId["provider:".Length..] : "local-vlm-ocr";
            var translationLegacyId = updatedTasks.Translation.ServiceId.StartsWith("provider:", StringComparison.Ordinal)
                ? updatedTasks.Translation.ServiceId["provider:".Length..] : "custom-chat";
            await _services.SaveSettingsAsync(current with
            {
                Configuration = current.Configuration with { Mode = mode, Tasks = updatedTasks },
                OcrProviderId = ocrLegacyId,
                TranslationProviderId = translationLegacyId
            });
            RefreshConfigurationCenter();
            SetInlineStatus(ConfigurationRoutesStatusText,
                UiText.IsEnglish ? "Task routing saved. QR decoding remains local." : "任务路由已保存。二维码解析仍在本地执行。",
                "SuccessTextBrush");
            SetGlobalStatus(UiText.IsEnglish ? "Task routing saved." : "任务路由已保存。", false);
        }
        catch (Exception exception)
        {
            SetInlineStatus(ConfigurationRoutesStatusText, UiText.Error(exception), "DangerTextBrush");
            SetGlobalStatus(UiText.Error(exception), true);
        }
    }
}

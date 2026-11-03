using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class SettingsWindow
{
    private Func<string, Task> _persistLanguage = _ => Task.CompletedTask;
    private string _selectedLanguage = UiText.CurrentLanguage;
    private bool _changingLanguage;
    internal Task LanguageChangeTask { get; private set; } = Task.CompletedTask;

    internal SettingsWindow(Func<string, Task> persistLanguage) : this() =>
        _persistLanguage = persistLanguage;

    private void InitializeLanguageSelection()
    {
        var loading = _isLoadingSettings;
        _isLoadingSettings = true;
        try
        {
            var choices = UiLanguageChoice.Create();
            UiLanguageCombo.ItemsSource = choices;
            UiLanguageCombo.SelectedItem = choices.First(c => c.Id == _selectedLanguage);
        }
        finally { _isLoadingSettings = loading; }
    }

    private async void UiLanguageCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || _changingLanguage || UiLanguageCombo.SelectedItem is not UiLanguageChoice choice)
            return;
        LanguageChangeTask = ChangeLanguageAsync(choice.Id);
        await LanguageChangeTask;
    }

    private async Task ChangeLanguageAsync(string language)
    {
        if (language == _selectedLanguage) return;
        _changingLanguage = true;
        UiLanguageCombo.IsEnabled = false;
        try
        {
            // Persist this one preference before changing the application language.
            // No model/credential refresh: that would overwrite unsaved secret editors.
            await _persistLanguage(language);
            _selectedLanguage = language;
            UiText.Configure(language);
            SetGlobalStatus(UiText.Get("String.LanguageApplied"), false);
        }
        catch (Exception exception)
        {
            InitializeLanguageSelection();
            SetGlobalStatus(UiText.Error(exception), true);
        }
        finally
        {
            UiLanguageCombo.IsEnabled = true;
            _changingLanguage = false;
        }
    }

    private void RefreshWindowTitle() =>
        Title = UiText.IsEnglish
            ? AppEdition.IsComplete ? "Screen Insight Complete Settings" : "Screen Insight Settings"
            : $"{AppEdition.ProductName}设置";

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshWindowTitle();
        RefreshRuntimeStatusLanguage();
        RefreshThirdPartyApiLanguage();
        RefreshConfigurationCenterLanguage();
        var loading = _isLoadingSettings;
        _isLoadingSettings = true;
        try
        {
            InitializeLanguageSelection();
            if (_services is not null)
            {
                // Keep the form's current choices and every editor instance. Do not call LoadSettings.
                var ocrId = (OcrProviderCombo.SelectedItem as ProviderChoice)?.Id;
                var translationId = (TranslationProviderCombo.SelectedItem as ProviderChoice)?.Id;
                var target = (TargetLanguageCombo.SelectedItem as LanguageChoice)?.Code ?? LanguageCatalog.AutoOpposite;
                var ocr = _services.Providers.OcrProviders.Select(p => new ProviderChoice(p.Metadata.Id,
                    UiText.ProviderName(p.Metadata.Id, p.Metadata.DisplayName))).ToArray();
                var translation = _services.Providers.TranslationProviders.Select(p => new ProviderChoice(p.Metadata.Id,
                    UiText.ProviderName(p.Metadata.Id, p.Metadata.DisplayName))).ToArray();
                OcrProviderCombo.ItemsSource = ocr;
                OcrProviderCombo.SelectedItem = ocr.FirstOrDefault(p => p.Id == ocrId);
                TranslationProviderCombo.ItemsSource = translation;
                TranslationProviderCombo.SelectedItem = translation.FirstOrDefault(p => p.Id == translationId);
                if (translationId is not null) LoadTargetLanguageChoices(translationId, target);
                UpdateManagedModelDescription();
                UpdateManagedRuntimeBackendDescription();
            }
            RefreshChoiceTemplates();
            foreach (var button in _buttonDefaultContents.Keys.ToArray())
                if (_buttonDefaultContents[button] is string text)
                    _buttonDefaultContents[button] = UiText.TranslateExisting(text);
            if (_deleteModelsDefaultContent is string deleteText)
                _deleteModelsDefaultContent = UiText.TranslateExisting(deleteText);
        }
        finally { _isLoadingSettings = loading; }
    }

    private void ThirdPartyApiPresetCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isLoadingSettings && ThirdPartyApiPresetCombo.SelectedItem is ThirdPartyApiPreset preset)
            ApiProtocolCombo.SelectedItem = ApiProtocolChoices.Get(preset.DefaultProtocol);
        RefreshCustomApiModeUi();
    }

    private void ApiProtocolCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        RefreshCustomApiModeUi();

    private void CustomEndpointBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (AppSettings.TryParseChatCompletionsEndpoint(CustomEndpointBox.Text, out var endpoint) &&
            endpoint.IsLoopback && ThirdPartyApiPresetCombo.SelectedItem is not null)
        {
            var loading = _isLoadingSettings;
            _isLoadingSettings = true;
            try
            {
                ThirdPartyApiPresetCombo.SelectedItem = null;
                ApiProtocolCombo.SelectedItem = ApiProtocolChoices.Get(ModelApiProtocols.ChatCompletions);
            }
            finally { _isLoadingSettings = loading; }
        }

        RefreshCustomApiModeUi();
    }

    private bool IsThirdPartyApiMode()
    {
        if (ThirdPartyApiPresetCombo.SelectedItem is ThirdPartyApiPreset)
            return true;
        return AppSettings.TryParseChatCompletionsEndpoint(CustomEndpointBox.Text, out var endpoint) &&
               !endpoint.IsLoopback;
    }

    private void RefreshCustomApiModeUi()
    {
        var apiMode = IsThirdPartyApiMode();
        if (!apiMode && ApiProtocolCombo.SelectedItem is ApiProtocolChoice { Id: ModelApiProtocols.Responses })
        {
            var wasLoading = _isLoadingSettings;
            _isLoadingSettings = true;
            try { ApiProtocolCombo.SelectedItem = ApiProtocolChoices.Get(ModelApiProtocols.ChatCompletions); }
            finally { _isLoadingSettings = wasLoading; }
        }
        LocalServicePresetCombo.IsEnabled = !apiMode;
        UseLocalLlamaPresetButton.IsEnabled = !apiMode;
        LocalServicePresetModeHintText.IsVisible = apiMode;
        LocalServicePresetModeHintText.Text = UiText.IsEnglish
            ? "API mode is selected. Local service presets are disabled; switch back to a loopback endpoint to use llama.cpp, Ollama or LM Studio."
            : "当前为 API 模式，本地服务预设已关闭。若要使用 llama.cpp、Ollama 或 LM Studio，请切换回本机回环地址。";
        ApiProtocolStack.IsVisible = apiMode;
        CustomModelLabelText.Text = UiText.IsEnglish
            ? apiMode ? "Model name (load from API)" : "Model name"
            : apiMode ? "模型名称（通过 API 获取）" : "模型名称";
        RefreshCustomModelsButton.Content = UiText.IsEnglish ? "Refresh model list" : "刷新模型列表";
        if (string.IsNullOrWhiteSpace(CustomModelStatusText.Text))
        {
            CustomModelStatusText.Text = UiText.IsEnglish
                ? "Fetch the models available to this API key, or type a model ID manually."
                : "可从 API 获取当前密钥可用的模型列表，也可以手动输入模型 ID。";
        }
    }

    private void RefreshThirdPartyApiLanguage()
    {
        ThirdPartyApiLabelText.Text = UiText.IsEnglish
            ? "Third-party API provider (OpenAI-compatible)"
            : "第三方 API 服务商（OpenAI 兼容）";
        ApplyThirdPartyApiPresetButton.Content = UiText.IsEnglish
            ? "Use provider endpoint"
            : "填入服务商地址";
        ApiProtocolLabelText.Text = UiText.IsEnglish ? "API protocol" : "API 协议";
        ThirdPartyApiHintText.Text = UiText.IsEnglish
            ? "OpenAI defaults to the Responses API; other provider presets use Chat Completions. You can change the protocol for compatible endpoints. Save the API Key, then refresh the model list. Local service presets are disabled in API mode and re-enabled for loopback endpoints. Replace the shared key whenever you switch providers. Remote translation sends recognized text; vision tasks send the selected screenshot and may incur charges. Remote endpoints require HTTPS; keys are stored in the OS secure store."
            : "OpenAI 预设默认使用 Responses API，其他服务商默认使用 Chat Completions。兼容的自定义端点可手动切换协议。保存 API Key 后刷新模型列表。本地 API 模式会关闭本地服务预设，切回回环地址后重新启用。切换服务商时请替换共用 API Key。远程翻译会发送识别文字，视觉任务会发送所选截图，可能产生费用。远程地址必须使用 HTTPS；密钥保存在系统安全存储中。";
    }

    private void RefreshChoiceTemplates()
    {
        // Core catalog records use CurrentUICulture. Freeze their display strings here
        // so a later UI dispatcher turn cannot inherit a stale async culture context.
        foreach (var combo in new[] { ThirdPartyApiPresetCombo, ApiProtocolCombo, LocalServicePresetCombo, ManagedModelCombo, ManagedRuntimeBackendCombo })
        {
            var labels = combo.Items.Cast<object>().ToDictionary(item => item, item => UiText.T(item.ToString()));
            combo.ItemTemplate = new FuncDataTemplate<object>((item, _) => new TextBlock
            {
                Text = item is not null && labels.TryGetValue(item, out var label) ? label : string.Empty,
                TextWrapping = TextWrapping.Wrap
            });
        }
    }
}

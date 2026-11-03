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

    private void RefreshThirdPartyApiLanguage()
    {
        ThirdPartyApiLabelText.Text = UiText.IsEnglish
            ? "Third-party API provider (OpenAI-compatible)"
            : "第三方 API 服务商（OpenAI 兼容）";
        ApplyThirdPartyApiPresetButton.Content = UiText.IsEnglish
            ? "Use provider endpoint"
            : "填入服务商地址";
        ThirdPartyApiHintText.Text = UiText.IsEnglish
            ? "Choose a provider, then enter the exact model ID and API Key below. Replace the key whenever you switch providers; the key field is shared. Save and test the text API. For screenshot OCR, image description or prompt reconstruction, also select Compatible multimodal LLM OCR and use a vision-capable model. Remote requests send recognized text or the selected image to that provider and may incur charges. Remote endpoints must use HTTPS; keys are stored in the OS secure store."
            : "选择服务商后，在下方填写该平台的准确模型 ID 和 API Key。切换服务商时请替换 API Key；当前密钥输入框由这些兼容接口共用。保存后测试翻译接口。若要通过 API 进行截图 OCR、图片描述或提示词反推，还需在“文字识别”中选择“兼容多模态大模型 OCR”，并使用支持图片输入的模型。远程翻译会发送识别文字，视觉任务会发送所选截图给服务商，可能产生费用。远程地址必须使用 HTTPS；密钥保存在系统安全存储中。";
    }

    private void RefreshChoiceTemplates()
    {
        // Core catalog records use CurrentUICulture. Freeze their display strings here
        // so a later UI dispatcher turn cannot inherit a stale async culture context.
        foreach (var combo in new[] { ThirdPartyApiPresetCombo, LocalServicePresetCombo, ManagedModelCombo, ManagedRuntimeBackendCombo })
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

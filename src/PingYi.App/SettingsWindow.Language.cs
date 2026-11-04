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
        RefreshWindowTitle(); RefreshSettingsLanguage(); InitializeLanguageSelection(); RefreshHotkeyEditor();
        ConfigurationCenter.RefreshLanguage(); LocalModelEditor.RefreshLanguage();
        var selected = (TargetLanguageCombo.SelectedItem as LanguageChoice)?.Code ?? LanguageCatalog.AutoOpposite;
        LoadTargetLanguageChoices(selected);
        UpdateManagedModelDescription(); UpdateManagedRuntimeBackendDescription();
    }
    private void RefreshSettingsLanguage()
    {
        CurrentModelSettingsTab.Header = CurrentModelTitle.Text = CaptureUiText.Pick("当前模型", "Current model");
        CloudSettingsTab.Header = CaptureUiText.Pick("云端 API 管理", "Cloud APIs");
        LocalSettingsTab.Header = CaptureUiText.Pick("本地模型管理", "Local models");
        HotkeySettingsTab.Header = CaptureUiText.Pick("快捷键", "Hotkeys");
        GeneralSettingsTab.Header = CaptureUiText.Pick("通用设置", "General");
        CurrentModelHint.Text = CaptureUiText.Pick("截图识字、翻译、图片描述与提示词反推共用此模型。请先在云端 API 或本地模型管理中测试文字与图片能力。未验证和纯文本模型不能设为当前模型。",
            "Recognition, translation, image description and prompt reconstruction share this model. Test text and image support under Cloud APIs or Local models first. Unverified and text-only models cannot be selected.");
        RefreshCurrentModelsButton.Content = CaptureUiText.Pick("刷新可用模型", "Refresh verified models");
        TargetLanguageLabel.Text = CaptureUiText.Pick("翻译目标语言", "Translation target language");
        LocalModelHint.Text = CaptureUiText.Pick("连接已有的 Ollama、LM Studio 或兼容服务；也可在下方下载并管理本软件托管的模型与推理后端。外部服务的下载和启停由其自身应用管理。",
            "Connect Ollama, LM Studio or a compatible local service, or download and manage an app-owned model and runtime below. External services manage their own downloads and lifecycle.");
        UiLanguageLabel.Text = CaptureUiText.Pick("界面语言", "Interface language");
        Avalonia.Automation.AutomationProperties.SetName(SaveSettingsButton, CaptureUiText.Pick("保存并应用", "Save and apply"));
        SaveSettingsButton.Content = CaptureUiText.Pick("保存并应用", "Save and apply");
    }
}

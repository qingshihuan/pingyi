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
        ConfigurationCenter.RefreshLanguage();
        var selected = (TargetLanguageCombo.SelectedItem as LanguageChoice)?.Code ?? LanguageCatalog.AutoOpposite;
        LoadTargetLanguageChoices(selected);
        UpdateManagedModelDescription(); UpdateManagedRuntimeBackendDescription();
    }
    private void RefreshSettingsLanguage()
    {
        CurrentModelSettingsTab.Header = CurrentModelTitle.Text = CaptureUiText.Pick("当前模型", "Current model");
        ApiSettingsTab.Header = CaptureUiText.Pick("API 模型管理", "API models");
        LocalSettingsTab.Header = CaptureUiText.Pick("本地模型管理", "Local models");
        HotkeySettingsTab.Header = CaptureUiText.Pick("快捷键", "Hotkeys");
        GeneralSettingsTab.Header = CaptureUiText.Pick("通用设置", "General");
        ResetCaptureApprovalsButton.Content = CaptureUiText.Pick("恢复远程发送确认", "Reset remote transfer prompts");
        CaptureApprovalsHint.Text = CaptureUiText.Pick("清除“同意并不再提示”的记录。下次向 API 发送截图或识别文字前会重新询问，立即生效。",
            "Clear remembered approvals. The next API transfer of a screenshot or recognized text will ask again. Takes effect immediately.");
        CurrentModelHint.Text = CaptureUiText.Pick("截图识字、翻译、图片描述与提示词反推共用此模型。请先在 API 模型管理中测试接口模型，或在本地模型管理中下载并验证本机模型。未验证和纯文本模型不能设为当前模型。",
            "Recognition, translation, image description and prompt reconstruction share this model. Test an endpoint under API models, or download and verify a model under Local models. Unverified and text-only models cannot be selected.");
        RefreshCurrentModelsButton.Content = CaptureUiText.Pick("刷新可用模型", "Refresh verified models");
        TargetLanguageLabel.Text = CaptureUiText.Pick("翻译目标语言", "Translation target language");
        ApiModelHint.Text = CaptureUiText.Pick("统一管理本地和云端 API 模型，包括 Ollama、LM Studio 与兼容接口。这里连接已有服务；模型下载、推理后端和显卡设置在“本地模型管理”中。",
            "Manage local and cloud API models together, including Ollama, LM Studio and compatible endpoints. Connect existing services here; app-owned downloads, runtimes and GPUs are under Local models.");
        LocalModelHint.Text = CaptureUiText.Pick("软件基础功能：下载并运行本机多模态模型，管理推理后端与显卡，无需云端 API。本地 API 服务调用请在“API 模型管理”中配置。",
            "Built-in functionality: download and run a local multimodal model and manage its runtime and GPU, without a cloud API. Configure existing local API services under API models.");
        AutomaticLocalSetupButton.Content = CaptureUiText.Pick("一键检测并自动配置", "Detect and configure automatically");
        AutomaticLocalSetupCancelButton.Content = CaptureUiText.Pick("取消", "Cancel");
        AutomaticLocalSetupHint.Text = CaptureUiText.Pick(
            "自动检测显卡、准备后端和模型，验证识字与翻译后保存并设为当前模型。显卡不可用时使用 CPU。已有模型优先复用，首次下载推荐模型约 1.82 GiB。",
            "Detect hardware, prepare the runtime and model, then verify recognition and translation before saving and selecting it. Use CPU when GPUs are unavailable. Reuse your configured model; the first recommended download is about 1.82 GiB.");
        Avalonia.Automation.AutomationProperties.SetName(AutomaticLocalSetupButton, AutomaticLocalSetupButton.Content.ToString());
        Avalonia.Automation.AutomationProperties.SetName(AutomaticLocalSetupCancelButton, AutomaticLocalSetupCancelButton.Content.ToString());
        UiLanguageLabel.Text = CaptureUiText.Pick("界面语言", "Interface language");
        Avalonia.Automation.AutomationProperties.SetName(SaveSettingsButton, CaptureUiText.Pick("保存并应用", "Save and apply"));
        SaveSettingsButton.Content = CaptureUiText.Pick("保存并应用", "Save and apply");
    }
}

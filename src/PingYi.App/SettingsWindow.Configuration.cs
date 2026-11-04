using Avalonia;
using Avalonia.Controls;
using PingYi.Core;

namespace PingYi.App;

public partial class SettingsWindow
{
    internal ConfigurationCenterView ConfigurationCenter { get; private set; } = null!;
    private Expander? _legacyConfiguration;

    private void InitializeConfigurationCenter()
    {
        ConfigurationCenter = new ConfigurationCenterView();
        ConfigurationCenter.Navigate += index => SettingsTabs.SelectedIndex = index;
        if (CustomSettingsTab.Content is ScrollViewer { Content: StackPanel page } && page.Children.Count >= 2)
        {
            var oldEditor = page.Children[1];
            page.Children.RemoveAt(1);
            _legacyConfiguration = new Expander { Content = oldEditor, IsExpanded = false, Margin = new Thickness(0, 16, 0, 0) };
            page.Children.Add(ConfigurationCenter);
            page.Children.Add(_legacyConfiguration);
        }
        RefreshConfigurationLanguage();
        UiText.LanguageChanged += ConfigurationLanguageChanged;
        Closed += (_, _) =>
        {
            UiText.LanguageChanged -= ConfigurationLanguageChanged;
            ConfigurationCenter.Cancel();
        };
    }
    private void BindConfigurationCenter()
    {
        if (_services is null) return;
        ConfigurationCenter.CompatibilityDraft = () => BuildSettingsFromForm() with
            { CustomTranslationEndpoint = CustomEndpointBox.Text ?? "" };
        ConfigurationCenter.Load(_services.Settings);
        ConfigurationCenter.Discover = _services.DiscoverProfileModelsAsync;
        ConfigurationCenter.Verify = _services.TestProfileModelAsync;
        if (_services.ConfigurationCredentialWarning is { } warning) SetGlobalStatus(warning, true);
    }
    private void ConfigurationLanguageChanged(object? sender, EventArgs e) => RefreshConfigurationLanguage();
    private void RefreshConfigurationLanguage()
    {
        Resources["Workspace.Custom"] = UiText.IsEnglish ? "Configuration center" : "配置中心";
        Resources["Workspace.CustomHint"] = UiText.IsEnglish
            ? "Independent services, scoped credentials and a model route for each task."
            : "服务独立保存，密钥互不串用，每个任务分别选择模型。";
        if (_legacyConfiguration is not null) _legacyConfiguration.Header = UiText.IsEnglish
            ? "Compatibility settings · used by Follow existing configuration"
            : "旧版兼容配置 · 仅供“沿用原配置”任务使用";
        ConfigurationCenter.RefreshLanguage();
    }
}

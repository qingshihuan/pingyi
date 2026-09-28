using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using PingYi.Core;

namespace PingYi.App;

public partial class MainWindow
{
    private readonly CheckBox _automaticCaptureToggle = new() { Name = "AutomaticCaptureToggle", IsChecked = true };
    private readonly Button _translateCaptureButton = new() { Name = "TranslateCaptureButton" };
    private readonly Button _modelSetupButton = new() { Name = "ModelSetupButton" };
    private bool _loadingAutomaticPreference;
    private FirstRunSetupWindow? _firstRunSetup;

    private void InitializeSmartCaptureUi()
    {
        if (DescribeImageButton.Parent is WrapPanel actions)
        {
            _translateCaptureButton.Classes.Add("secondary");
            _translateCaptureButton.MinHeight = 32;
            _translateCaptureButton.Padding = new Thickness(12, 5);
            _translateCaptureButton.Margin = new Thickness(0, 0, 8, 4);
            actions.Children.Insert(0, _translateCaptureButton);
        }
        if (CaptureButtonV2.Parent is StackPanel hero)
        {
            hero.Children.Insert(hero.Children.IndexOf(CaptureButtonV2), _automaticCaptureToggle);
            _modelSetupButton.Classes.Add("toolbar-action");
            hero.Children.Add(_modelSetupButton);
        }
        _translateCaptureButton.Click += async (_, _) => await CaptureImageAnalysisAsync(_translateCaptureButton, CapturePurpose.TranslateText);
        _modelSetupButton.Click += async (_, _) => await ShowInitialSetupAsync(true);
        _automaticCaptureToggle.IsCheckedChanged += async (_, _) =>
        {
            if (_loadingAutomaticPreference) return;
            try
            {
                if (_services is not null) await _services.SaveAutomaticCapturePreferenceAsync(_automaticCaptureToggle.IsChecked == true);
            }
            catch (Exception error) { SetGlobalStatus(UiText.Error(error), true); }
            RefreshSmartCaptureUi();
        };
        RefreshSmartCaptureUi();
    }
    private void RefreshSmartCaptureUi()
    {
        _loadingAutomaticPreference = true;
        try
        {
            _automaticCaptureToggle.IsChecked = _services?.Settings.AutomaticCaptureEnabled ?? true;
            _automaticCaptureToggle.Content = CaptureUiText.Pick("自动判断截图任务", "Automatically choose the capture task");
            _translateCaptureButton.Content = UiText.Get("String.TranslateText");
            _modelSetupButton.Content = CaptureUiText.Pick("基础模式 · 模型配置引导", "Basic mode · model setup");
            if (CaptureButtonV2.Content is Grid content)
                foreach (var text in content.Children.OfType<TextBlock>())
                    text.Text = _automaticCaptureToggle.IsChecked == true ? CaptureUiText.Automatic : UiText.Get("String.TranslateText");
            if (CaptureButtonV2.Parent is StackPanel hero)
            {
                var labels = hero.Children.OfType<TextBlock>().Take(2).ToArray();
                if (labels.Length == 2)
                {
                    labels[0].Text = CaptureUiText.Pick("一次截图，读懂内容", "One capture. Understand the content.");
                    labels[1].Text = CaptureUiText.Pick("自动选择翻译、图片描述或二维码解析；也可手动选择。", "Automatically select translation, image description or QR decoding — or choose manually.");
                    labels[1].TextWrapping = TextWrapping.Wrap;
                }
            }
            AutomationProperties.SetName(CaptureButtonV2, _automaticCaptureToggle.IsChecked == true ? CaptureUiText.Automatic : UiText.Get("String.TranslateText"));
            AutomationProperties.SetAutomationId(_automaticCaptureToggle, "AutomaticCaptureToggle");
            AutomationProperties.SetName(_translateCaptureButton, UiText.Get("String.TranslateText"));
            AutomationProperties.SetName(_modelSetupButton, _modelSetupButton.Content?.ToString() ?? "");
        }
        finally { _loadingAutomaticPreference = false; }
    }
    private async Task ShowInitialSetupAsync(bool force = false)
    {
        if (_services is null || (!force && _services.Settings.InitialSetupCompleted)) return;
        if (_firstRunSetup is not null) { _firstRunSetup.Activate(); return; }
        var setup = new FirstRunSetupWindow(_services);
        _firstRunSetup = setup;
        _services.IsInitialSetupActive = true;
        InitialSetupChoice choice;
        try { choice = await setup.ShowDialog<InitialSetupChoice>(this); }
        finally { _services.IsInitialSetupActive = false; _firstRunSetup = null; }
        LoadSettings();
        if (choice == InitialSetupChoice.ConfigureExisting) await OpenSettingsWindowAsync();
        await RefreshDashboardAsync();
    }
}

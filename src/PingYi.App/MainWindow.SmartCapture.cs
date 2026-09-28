using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using PingYi.Core;

namespace PingYi.App;

public partial class MainWindow
{
    private readonly CheckBox _automaticCaptureToggle = new() { Name = "AutomaticCaptureToggle", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
    private readonly Button _translateCaptureButton = new() { Name = "TranslateCaptureButton" };
    private readonly Button _modelSetupButton = new() { Name = "ModelSetupButton" };
    private bool _loadingAutomaticPreference;
    private FirstRunSetupWindow? _firstRunSetup;

    private void InitializeSmartCaptureUi()
    {
        if (DescribeImageButton.Parent is WrapPanel actions)
        {
            _translateCaptureButton.Classes.Add("secondary");
            actions.Children.Insert(0, _translateCaptureButton);
            foreach (var button in actions.Children.OfType<Button>())
            {
                button.MinHeight = 30;
                button.Padding = new Thickness(10, 4);
                button.Margin = new Thickness(0, 0, 6, 4);
            }
        }
        if (CaptureButtonV2.Parent is StackPanel hero)
        {
            hero.Spacing = 8;
            _modelSetupButton.Classes.Add("toolbar-action");
            hero.Children.Add(new WrapPanel { Children = { _automaticCaptureToggle, _modelSetupButton } });
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
            _automaticCaptureToggle.IsChecked = _services?.Settings.AutomaticCaptureEnabled ?? _automaticCaptureToggle.IsChecked;
            _automaticCaptureToggle.Content = CaptureUiText.Pick("自动判断任务", "Auto task");
            _translateCaptureButton.Content = CaptureUiText.Pick("文字翻译", "Translate");
            DescribeImageButton.Content = CaptureUiText.Pick("图片描述", "Describe");
            ReconstructPromptButton.Content = CaptureUiText.Pick("反推提示词", "Prompt");
            QrCodeButton.Content = CaptureUiText.Pick("二维码", "QR code");
            _modelSetupButton.Content = CaptureUiText.Pick("模型配置引导", "Model setup");
            if (CaptureButtonV2.Content is Grid content)
                foreach (var text in content.Children.OfType<TextBlock>())
                    text.Text = _automaticCaptureToggle.IsChecked == true ? CaptureUiText.Automatic : UiText.Get("String.TranslateText");
            if (CaptureButtonV2.Parent is StackPanel hero)
            {
                var labels = hero.Children.OfType<TextBlock>().ToArray();
                if (labels.Length >= 2)
                {
                    labels[0].Text = CaptureUiText.Pick("一次截图，读懂内容", "Capture and understand");
                    labels[1].Text = CaptureUiText.Pick("翻译、图片描述或二维码解析，可随时手动改选。", "Translate, describe or decode QR — you can override the choice.");
                    labels[1].TextWrapping = TextWrapping.Wrap;
                }
                if (labels.Length >= 3)
                    labels[2].Text = CaptureUiText.Pick("图片描述与基础模式需要视觉模型。", "Basic and image description need a vision model.");
            }
            AutomationProperties.SetName(CaptureButtonV2, _automaticCaptureToggle.IsChecked == true ? CaptureUiText.Automatic : UiText.Get("String.TranslateText"));
            AutomationProperties.SetAutomationId(_automaticCaptureToggle, "AutomaticCaptureToggle");
            AutomationProperties.SetName(_translateCaptureButton, UiText.Get("String.TranslateText"));
            AutomationProperties.SetName(_modelSetupButton, CaptureUiText.Pick("基础模式模型配置引导", "Configure a local model for Basic mode"));
            ToolTip.SetTip(DescribeImageButton, UiText.Get("String.DescribeImage"));
            ToolTip.SetTip(ReconstructPromptButton, UiText.Get("String.ReconstructPrompt"));
            ToolTip.SetTip(QrCodeButton, UiText.Get("String.DecodeQrCode"));
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

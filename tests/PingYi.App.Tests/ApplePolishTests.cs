using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using PingYi.App;
using Xunit;

namespace PingYi.App.Tests;

public class ApplePolishTests
{
    [AvaloniaTheory]
    [InlineData(false, "zh-CN")]
    [InlineData(true, "zh-CN")]
    [InlineData(false, "en-US")]
    [InlineData(true, "en-US")]
    public void Design_previews_use_explicitly_labeled_synthetic_data(bool dark, string language)
    {
        UiText.Configure(language);
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var suffix = $"{language}-{(dark ? "dark" : "light")}";
        var home = new MainWindowV2 { RequestedThemeVariant = theme };
        var settings = new MainWindow { RequestedThemeVariant = theme };
        var result = new ResultWindow { RequestedThemeVariant = theme };
        try
        {
            C<TextBlock>(home, "TopStatusText").Text = "UI Preview";
            C<TextBlock>(home, "ModelStatusTitleText").Text = UiText.IsEnglish ? "Load on demand" : "按需加载";
            C<TextBlock>(home, "ModelStatusDetailText").Text = UiText.IsEnglish ? "Sample local OCR and translation setup." : "本地识别与离线翻译方案示例";
            C<TextBlock>(home, "PrivacySummaryText").Text = UiText.IsEnglish ? "Local-mode example. Screenshots and text stay on this device." : "本地方案示例：截图与文字只在本机处理。";
            C<TextBlock>(home, "LiveStatusTitleText").Text = "UI Preview";
            C<TextBlock>(home, "LiveStatusDetailText").Text = WorkspaceText.Preview;
            home.Show();
            SaveFrame(home, $"refined-home-{suffix}");

            C<Border>(settings, "CaptureHero").IsVisible = false;
            C<TextBlock>(settings, "WindowHeadingText").Text = WorkspaceText.Settings;
            C<TextBlock>(settings, "GlobalStatusText").Text = WorkspaceText.Preview;
            SetChoices(settings, "OcrProviderCombo", "PaddleOCR · ONNX");
            SetChoices(settings, "TranslationProviderCombo", "Argos · Offline");
            SetChoices(settings, "TargetLanguageCombo", UiText.IsEnglish ? "Auto (Chinese / English)" : "自动 · 中英互译");
            SetChoices(settings, "InterfaceStyleCombo", UiText.IsEnglish ? "Capture workspace" : "截图工作台");
            SetChoices(settings, "UiLanguageCombo", UiText.IsEnglish ? "English" : "简体中文");
            SetChoices(settings, "LocalServicePresetCombo", "llama.cpp");
            C<TextBox>(settings, "HotkeyBox").Text = "Ctrl+Alt+D";
            C<TextBox>(settings, "CustomEndpointBox").Text = "http://127.0.0.1:8080/v1/chat/completions";
            settings.Show();
            var tabs = C<TabControl>(settings, "SettingsTabs");
            for (var i = 0; i < 5; i++)
            {
                tabs.SelectedIndex = i;
                SaveFrame(settings, $"refined-settings-{i}-{suffix}");
            }

            result.SetLoading("UI Preview", WorkspaceText.Preview);
            C<ProgressBar>(result, "ProcessingProgress").IsVisible = false;
            C<TextBox>(result, "SourceTextBox").Text = "A little clarity changes the way you see the world.\nSelect the words that matter, and keep reading.";
            C<TextBox>(result, "TranslationTextBox").Text = "多一点理解，看世界就多一种方式。\n框选你在意的文字，让阅读自然继续。";
            result.Show();
            SaveFrame(result, $"refined-result-{suffix}");
        }
        finally { home.Close(); settings.Close(); result.ClosePermanently(); }
    }

    [AvaloniaTheory]
    [InlineData(false, "zh-CN")]
    [InlineData(true, "en-US")]
    public void Result_error_actions_and_text_areas_fit_minimum_size(bool dark, string language)
    {
        UiText.Configure(language);
        var result = new ResultWindow { Width = 460, Height = 440,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            result.SetError(new string('x', 300), keepSource: true);
            result.Show();
            result.UpdateLayout();
            foreach (var name in new[] { "RepairButton", "SourceTextBox", "TranslationTextBox", "PinButton" })
                AssertFits(result, C<Control>(result, name));
            Assert.True(C<TextBox>(result, "SourceTextBox").IsReadOnly);
            Assert.True(C<TextBox>(result, "TranslationTextBox").IsReadOnly);
            Assert.False(C<ProgressBar>(result, "ProcessingProgress").IsVisible);
            SaveFrame(result, $"refined-result-min-{language}-{dark}");
        }
        finally { result.ClosePermanently(); }
    }

    [AvaloniaFact]
    public void Sidebar_keeps_native_keyboard_navigation_and_accessible_controls()
    {
        UiText.Configure("en-US");
        var window = new MainWindow { Width = 800, Height = 560 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var first = C<TabItem>(window, "GeneralSettingsTab");
            Assert.True(first.Focus());
            window.KeyPress(Key.Down);
            Assert.Equal(1, C<TabControl>(window, "SettingsTabs").SelectedIndex);
            AssertFits(window, C<Button>(window, "SaveSettingsButton"));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(C<Button>(window, "SaveSettingsButton"))));
        }
        finally { window.Close(); }
    }

    private static void SetChoices(Window window, string name, string value)
    {
        var combo = C<ComboBox>(window, name);
        combo.ItemsSource = new[] { value };
        combo.SelectedIndex = 0;
    }

    private static T C<T>(Window window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException(name);

    private static void AssertFits(Window window, Control control)
    {
        var position = control.TranslatePoint(default, window);
        Assert.True(position.HasValue);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Assert.True(position.Value.X >= 0 && position.Value.Y >= 0);
        Assert.True(position.Value.X + control.Bounds.Width <= window.ClientSize.Width + 1);
        Assert.True(position.Value.Y + control.Bounds.Height <= window.ClientSize.Height + 1);
    }

    private static void SaveFrame(Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
}

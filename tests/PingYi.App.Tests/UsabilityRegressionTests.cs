using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PingYi.Core;
using PingYi.Infrastructure;
using Xunit;

namespace PingYi.App.Tests;

public class UsabilityRegressionTests
{
    [AvaloniaFact]
    public async Task Selecting_a_language_persists_and_updates_all_open_chrome_without_touching_user_data()
    {
        UiText.Configure(UiText.Chinese);
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-language-" + Guid.NewGuid().ToString("N"));
        var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
        var persisted = new AppSettings { UiLanguage = UiText.Chinese, CustomTranslationModel = "stored-model" };
        await store.SaveAsync(persisted);
        var settings = new SettingsWindow(async language =>
        {
            persisted = persisted with { UiLanguage = language };
            await store.SaveAsync(persisted);
        });
        var home = new MainWindow();
        var result = new ResultWindow();
        var help = new HelpAboutWindow(() => persisted);
        try
        {
            home.Show(); settings.Show(); result.Show(); help.Show();
            C<TabControl>(settings, "SettingsTabs").SelectedIndex = 4;
            C<TextBox>(settings, "CustomModelBox").Text = "unsaved-model-模型";
            C<TextBox>(settings, "HotkeyBox").Text = "Ctrl+Alt+G";
            C<TextBox>(settings, "CustomApiKeyBox").Text = "unsaved-test-key";
            C<TextBox>(result, "SourceTextBox").Text = "开始截图";
            C<TextBox>(result, "TranslationTextBox").Text = "保存并应用";
            var input = C<TextBox>(settings, "CustomModelBox");
            C<ComboBox>(settings, "UiLanguageCombo").SelectedIndex = 2; // Real SelectionChanged handler.
            await settings.LanguageChangeTask;
            settings.UpdateLayout(); home.UpdateLayout(); help.UpdateLayout();
            Assert.Equal("en-US", (await store.LoadAsync()).UiLanguage);
            Assert.Equal("PingYi", Application.Current!.Resources["String.AppName"]);
            Assert.Equal("Save and apply", C<Button>(settings, "SaveSettingsButton").Content);
            Assert.Equal("Help & About", C<Button>(home, "HelpButton").Content);
            Assert.Contains(settings.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Appearance & startup");
            Assert.Equal(4, C<TabControl>(settings, "SettingsTabs").SelectedIndex);
            Assert.Same(input, C<TextBox>(settings, "CustomModelBox"));
            Assert.Equal("unsaved-model-模型", input.Text);
            Assert.Equal("unsaved-test-key", C<TextBox>(settings, "CustomApiKeyBox").Text);
            Assert.Equal("Ctrl+Alt+G", C<TextBox>(settings, "HotkeyBox").Text);
            Assert.Equal("开始截图", C<TextBox>(result, "SourceTextBox").Text);
            Assert.Equal("保存并应用", C<TextBox>(result, "TranslationTextBox").Text);
            Frame(settings, "fixed-language-en");
            C<ComboBox>(settings, "UiLanguageCombo").SelectedIndex = 1;
            await settings.LanguageChangeTask;
            Assert.Equal("zh-CN", (await store.LoadAsync()).UiLanguage);
            Assert.Equal("屏译", Application.Current.Resources["String.AppName"]);
            Assert.Equal("保存并应用", C<Button>(settings, "SaveSettingsButton").Content);
            Assert.Equal("帮助与关于", C<Button>(home, "HelpButton").Content);
            Assert.Equal("unsaved-model-模型", input.Text);
            Assert.Equal("stored-model", (await store.LoadAsync()).CustomTranslationModel);
        }
        finally
        {
            settings.Close(); home.Close(); result.ClosePermanently(); help.Close();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Failed_language_save_restores_selection_and_keeps_the_previous_language()
    {
        UiText.Configure(UiText.Chinese);
        var settings = new SettingsWindow(_ => Task.FromException(new IOException("test save failed")));
        try
        {
            settings.Show();
            C<ComboBox>(settings, "UiLanguageCombo").SelectedIndex = 2;
            await settings.LanguageChangeTask;
            Assert.Equal(UiText.Chinese, UiText.CurrentLanguage);
            Assert.Equal(1, C<ComboBox>(settings, "UiLanguageCombo").SelectedIndex);
            Assert.True(C<ComboBox>(settings, "UiLanguageCombo").IsEnabled);
            Assert.Equal("保存并应用", C<Button>(settings, "SaveSettingsButton").Content);
        }
        finally { settings.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("zh-CN", false)]
    [InlineData("en-US", true)]
    public void One_shell_no_style_selector_and_privacy_only_in_help(string language, bool dark)
    {
        UiText.Configure(language);
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var home = new MainWindow { RequestedThemeVariant = theme };
        var settings = new SettingsWindow { RequestedThemeVariant = theme };
        var help = new HelpAboutWindow(() => new AppSettings { OcrProviderId = "google-vision-ocr", TranslationProviderId = "google-translate" })
        { RequestedThemeVariant = theme };
        try
        {
            home.Show(); settings.Show(); help.Show();
            Assert.Null(typeof(AppSettings).GetProperty("InterfaceStyle"));
            Assert.Null(settings.FindControl<ComboBox>("InterfaceStyleCombo"));
            Assert.Null(settings.FindControl<Button>("OpenClassicInterfaceButton"));
            Assert.Null(settings.FindControl<Button>("CaptureButton"));
            Assert.Null(home.FindControl<TextBlock>("PrivacySummaryText"));
            Assert.NotNull(home.FindControl<Button>("HelpButton"));
            Assert.Contains("Google Cloud Vision", C<TextBlock>(help, "PrivacySummaryText").Text);
            Assert.Contains("Google Cloud Translation", C<TextBlock>(help, "PrivacySummaryText").Text);
            C<TextBlock>(home, "LiveStatusDetailText").Text = WorkspaceText.Preview;
            Frame(home, $"fixed-home-{language}");
            Frame(help, $"fixed-help-{language}");
        }
        finally { home.Close(); settings.Close(); help.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("zh-CN", false)]
    [InlineData("en-US", true)]
    public void Mode_cards_wrap_instead_of_clipping_and_apply_stays_visible(string language, bool dark)
    {
        UiText.Configure(language);
        var window = new ModePickerWindow(new AppSettings { CustomTranslationEndpoint = "https://example.com/v1/chat/completions" })
        { Width = 620, Height = 460, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var choices = C<StackPanel>(window, "ModeChoices");
            Assert.Equal(5, choices.Children.Count);
            foreach (var radio in choices.Children.Cast<RadioButton>())
            {
                var texts = ((StackPanel)radio.Content!).Children.Cast<TextBlock>().ToArray();
                Assert.Equal(4, texts.Length);
                Assert.All(texts, t =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(t.Text));
                    Assert.Equal(Avalonia.Media.TextWrapping.Wrap, t.TextWrapping);
                    var point = t.TranslatePoint(default, window)!.Value;
                    Assert.True(point.X >= 0 && point.X + t.Bounds.Width <= window.ClientSize.Width + 1);
                });
            }
            Assert.Contains(UiText.Get("String.RemoteVisionData"), ModePickerWindow.Requirements("vision", new AppSettings { CustomTranslationEndpoint = "https://example.com/v1" }));
            var apply = C<Button>(window, "ApplyModeButton");
            Assert.DoesNotContain(apply.GetVisualAncestors(), p => p is ScrollViewer);
            var position = apply.TranslatePoint(default, window)!.Value;
            Assert.True(position.Y >= 0 && position.Y + apply.Bounds.Height <= window.ClientSize.Height + 1);
            ((RadioButton)choices.Children[2]).IsChecked = true;
            Assert.Equal("vision", window.SelectedModeId);
            Frame(window, $"fixed-modes-{language}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_hides_every_visible_app_window_before_the_barrier_and_restores_on_success_or_error(bool fail)
    {
        var home = new MainWindow { Position = new PixelPoint(-1200, 40) };
        var settings = new SettingsWindow { Position = new PixelPoint(300, 70) };
        var pinned = new ResultWindow();
        var hidden = new HelpAboutWindow();
        try
        {
            home.Show(); settings.Show(); pinned.Show();
            var states = new Window[] { home, settings, pinned };
            var calls = new List<string>();
            async Task Run() => await CaptureWindowScope.RunAsync(new Window[] { home, settings, pinned, hidden, home },
                token =>
                {
                    Assert.All(states, w => Assert.False(w.IsVisible));
                    Assert.False(hidden.IsVisible);
                    calls.Add("barrier");
                    return Task.CompletedTask;
                }, token =>
                {
                    Assert.Equal(new[] { "barrier" }, calls);
                    Assert.All(states, w => Assert.False(w.IsVisible));
                    calls.Add("capture");
                    if (fail) throw new IOException("synthetic capture failure");
                    return Task.FromResult(true);
                }, CancellationToken.None);
            if (fail) await Assert.ThrowsAsync<IOException>(Run); else await Run();
            Assert.All(states, w => Assert.True(w.IsVisible));
            Assert.False(hidden.IsVisible);
            Assert.Equal(new[] { "barrier", "capture" }, calls);
        }
        finally { home.Close(); settings.Close(); pinned.ClosePermanently(); hidden.Close(); }
    }

    [AvaloniaFact]
    public async Task Cancellation_at_the_desktop_barrier_never_captures_or_leaves_windows_hidden()
    {
        var home = new MainWindow();
        using var cancellation = new CancellationTokenSource();
        try
        {
            home.Show();
            var captured = false;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await CaptureWindowScope.RunAsync(new[] { home }, token =>
                {
                    Assert.False(home.IsVisible);
                    cancellation.Cancel();
                    return Task.FromCanceled(token);
                }, token => { captured = true; return Task.FromResult(true); }, cancellation.Token));
            Assert.False(captured);
            Assert.True(home.IsVisible);
        }
        finally { home.Close(); }
    }

    private static T C<T>(Window window, string name) where T : Control => window.FindControl<T>(name)
        ?? throw new InvalidOperationException(name);
    private static void Frame(Window window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var path = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame); frame.Save(Path.Combine(path, name + ".png"));
    }
}

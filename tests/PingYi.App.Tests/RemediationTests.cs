using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PingYi.Core;
using PingYi.Infrastructure;
using Xunit;

namespace PingYi.App.Tests;

public class RemediationTests
{
    [AvaloniaFact]
    public async Task Selecting_language_saves_it_and_updates_existing_windows_both_ways_without_losing_drafts()
    {
        UiText.Configure("zh-CN");
        var path = Path.Combine(Path.GetTempPath(), "pingyi-language-" + Guid.NewGuid() + ".json");
        var store = new JsonSettingsStore(path);
        await store.SaveAsync(new AppSettings { UiLanguage = "zh-CN", CustomTranslationModel = "saved-model" });
        var settings = new MainWindow();
        var home = new MainWindowV2();
        var result = new ResultWindow();
        try
        {
            settings.PersistLanguageAsync = async language => await store.SaveAsync((await store.LoadAsync()) with { UiLanguage = language });
            var draft = C<TextBox>(settings, "CustomModelBox"); draft.Text = "unsaved-模型";
            var endpoint = C<TextBox>(settings, "CustomEndpointBox"); endpoint.Text = "http://127.0.0.1:8080/v1/chat/completions";
            var secret = C<TextBox>(settings, "CustomApiKeyBox"); secret.Text = "test-only-secret";
            C<TextBox>(result, "SourceTextBox").Text = "这是用户原文，不是界面标签。";
            C<TextBox>(result, "TranslationTextBox").Text = "Keep the actual translated content.";
            settings.Show(); home.Show(); result.Show();
            C<TabControl>(settings, "SettingsTabs").SelectedIndex = 4;
            await SelectLanguage(settings, "en-US");
            Assert.Equal("en-US", (await store.LoadAsync()).UiLanguage);
            Assert.Equal("saved-model", (await store.LoadAsync()).CustomTranslationModel);
            Assert.Equal("en-US", UiText.CurrentLanguage);
            Assert.Contains(Texts(settings), t => t == "Appearance & startup");
            Assert.Contains(Texts(home), t => t == "Help & About");
            Assert.DoesNotContain(Texts(settings), t => t == "外观与启动" || t == "保存并应用");
            Assert.Same(draft, C<TextBox>(settings, "CustomModelBox"));
            Assert.Equal("unsaved-模型", draft.Text);
            Assert.Equal("test-only-secret", secret.Text);
            Assert.Equal("这是用户原文，不是界面标签。", C<TextBox>(result, "SourceTextBox").Text);
            Assert.Equal("Keep the actual translated content.", C<TextBox>(result, "TranslationTextBox").Text);
            Frame(settings, "fixed-language-en");
            UiText.Configure((await store.LoadAsync()).UiLanguage);
            var reopened = new MainWindow();
            try { reopened.Show(); Assert.Contains(Texts(reopened), t => t == "Recognition & translation"); }
            finally { reopened.Close(); }
            await SelectLanguage(settings, "zh-CN");
            Assert.Contains(Texts(settings), t => t == "外观与启动");
            Assert.Contains(Texts(home), t => t == "帮助与关于");
            Assert.Equal("unsaved-模型", draft.Text);
            Assert.Equal("zh-CN", (await store.LoadAsync()).UiLanguage);
        }
        finally { settings.Close(); home.Close(); result.ClosePermanently(); File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task Failed_language_save_rolls_back_selection_and_does_not_change_the_visible_locale()
    {
        UiText.Configure("zh-CN");
        var window = new MainWindow { PersistLanguageAsync = _ => throw new IOException("synthetic save failure") };
        try
        {
            window.Show();
            await SelectLanguage(window, "en-US");
            Assert.Equal("zh-CN", UiText.CurrentLanguage);
            Assert.Equal("zh-CN", Id(C<ComboBox>(window, "UiLanguageCombo").SelectedItem!));
            Assert.True(C<ComboBox>(window, "UiLanguageCombo").IsEnabled);
            Assert.Contains("synthetic save failure", C<TextBlock>(window, "GlobalStatusText").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Capture_scope_hides_owner_settings_and_pinned_results_before_the_barrier_then_restores_only_visible_windows()
    {
        var main = new Window { Position = new PixelPoint(-1200, 20) };
        var settings = new Window { Position = new PixelPoint(100, 80) };
        var pinned = new ResultWindow();
        C<Avalonia.Controls.Primitives.ToggleButton>(pinned, "PinButton").IsChecked = true;
        var hidden = new Window();
        try
        {
            main.Show(); var dialog = DialogPresentation.ShowAsync(settings, main); pinned.Show();
            var position = main.Position;
            using (var scope = new CaptureVisibilityScope(new Window[] { main, settings, pinned, hidden }, nativeVisibility: (_, _) => { }))
            {
                Assert.True(CaptureVisibilityScope.IsActive);
                await scope.WaitForDesktopAsync(default, _ =>
                {
                    Assert.True(scope.IsConcealed(main)); Assert.True(scope.IsConcealed(settings)); Assert.True(scope.IsConcealed(pinned));
                    Assert.False(dialog.IsCompleted); Assert.Same(main, settings.Owner);
                    return Task.CompletedTask;
                });
                Assert.True(pinned.IsPinned);
            }
            Assert.True(main.IsVisible); Assert.True(settings.IsVisible); Assert.True(pinned.IsVisible);
            Assert.False(hidden.IsVisible); Assert.Equal(position, main.Position);
            Assert.False(dialog.IsCompleted); Assert.Same(main, settings.Owner);
            Assert.False(CaptureVisibilityScope.IsActive);
            using (var scope = new CaptureVisibilityScope(new[] { main, settings, pinned }, nativeVisibility: (_, _) => { }))
            {
                using var canceled = new CancellationTokenSource(); canceled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.WaitForDesktopAsync(canceled.Token, _ => Task.CompletedTask));
            }
            Assert.True(main.IsVisible); Assert.True(settings.IsVisible);
            using (var scope = new CaptureVisibilityScope(new[] { main, settings, pinned }, nativeVisibility: (_, _) => { })) scope.Restore(showWindows: false);
            Assert.False(CaptureVisibilityScope.IsActive);
            settings.Close(); await dialog;
        }
        finally { settings.Close(); main.Close(); hidden.Close(); pinned.ClosePermanently(); }
    }

    [AvaloniaTheory]
    [InlineData("zh-CN", false)]
    [InlineData("en-US", true)]
    public void Scheme_picker_wraps_full_descriptions_and_help_contains_privacy_instead_of_home(string language, bool dark)
    {
        UiText.Configure(language);
        var picker = new ModePickerWindow { Width = 540, Height = 650, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        var home = new MainWindowV2();
        var help = new HelpWindow(new AppSettings { TranslationProviderId = "custom-chat", CustomTranslationEndpoint = "https://user:secret@example.com/private-key?api_key=private" });
        try
        {
            picker.Show(); home.Show(); help.Show();
            var list = C<ListBox>(picker, "ModesList");
            Assert.Equal(6, list.Items.Count);
            foreach (var mode in ModeChoice.All)
            {
                list.SelectedItem = mode; list.ScrollIntoView(mode);
                picker.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.False(string.IsNullOrWhiteSpace(mode.Purpose));
                Assert.False(string.IsNullOrWhiteSpace(mode.Requirements));
                var container = list.ContainerFromItem(mode);
                Assert.NotNull(container);
                foreach (var text in container.GetVisualDescendants().OfType<TextBlock>())
                {
                    Assert.Equal(Avalonia.Media.TextWrapping.Wrap, text.TextWrapping);
                    Assert.True(text.Bounds.Width <= container.Bounds.Width + 1);
                }
            }
            Assert.Null(home.FindControl<TextBlock>("PrivacySummaryText"));
            Assert.DoesNotContain(home.GetLogicalDescendants(), c => c is MenuFlyout || c is MenuItem);
            var privacy = C<TextBlock>(help, "DataFlowText").Text!;
            Assert.Contains("example.com", privacy); Assert.DoesNotContain("private", privacy); Assert.DoesNotContain("secret", privacy);
            var preferences = new MainWindow();
            try { Assert.Null(preferences.FindControl<ComboBox>("InterfaceStyleCombo")); }
            finally { preferences.Close(); }
            list.SelectedIndex = 0; list.ScrollIntoView(list.SelectedItem!);
            Frame(picker, $"fixed-schemes-{language}"); Frame(help, $"fixed-help-{language}");
        }
        finally { picker.Close(); home.Close(); help.Close(); }
    }

    private static IEnumerable<string?> Texts(Window w) => w.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text)
        .Concat(w.GetLogicalDescendants().OfType<ContentControl>().Select(c => c.Content as string));
    private static string Id(object choice) => (string)choice.GetType().GetProperty("Id")!.GetValue(choice)!;
    private static async Task SelectLanguage(MainWindow w, string id)
    {
        var combo = C<ComboBox>(w, "UiLanguageCombo");
        combo.SelectedItem = combo.Items.Cast<object>().Single(c => Id(c) == id);
        await w.LanguageChangeTask;
        w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
    }
    private static T C<T>(Window w, string name) where T : Control => w.FindControl<T>(name) ?? throw new InvalidOperationException(name);
    private static void Frame(Window w, string name)
    {
        w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = w.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
}

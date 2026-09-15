using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PingYi.App;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(PingYi.App.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PingYi.App.Tests;

public static class TestAppBuilder
{
    // No desktop lifetime: never initialize capture, models, accounts or networking.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::PingYi.App.App>()
        .WithInterFont()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class WorkspaceTests
{
    [AvaloniaTheory]
    [InlineData(800, 560, false, "zh-CN")]
    [InlineData(800, 560, true, "en-US")]
    [InlineData(1040, 720, false, "zh-CN")]
    [InlineData(1040, 720, true, "en-US")]
    public void Capture_and_feedback_remain_visible(int width, int height, bool dark, string language)
    {
        UiText.Configure(language);
        var window = new MainWindowV2 { Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            window.UpdateLayout();
            AssertVisibleWithinWindow(window, Required<Button>(window, "CaptureButtonV2"));
            AssertVisibleWithinWindow(window, Required<TextBlock>(window, "LiveStatusTitleText"));
            Capture(window, $"home-{width}-{language}-{dark}");
            window.SetGlobalStatus(new string('x', 500), true);
            window.UpdateLayout();
            Assert.True(Required<Border>(window, "RecoveryBorder").IsVisible);
            AssertVisibleWithinWindow(window, Required<TextBlock>(window, "LiveStatusTitleText"));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(800, 560, false, "zh-CN")]
    [InlineData(800, 560, true, "en-US")]
    [InlineData(1040, 760, false, "zh-CN")]
    [InlineData(1040, 760, true, "en-US")]
    public void Settings_pages_keep_fields_and_fixed_save(int width, int height, bool dark, string language)
    {
        UiText.Configure(language);
        var window = new MainWindow { Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        // Equivalent visual state to settingsMode, without creating real AppServices.
        Required<Border>(window, "CaptureHero").IsVisible = false;
        var tabs = Required<TabControl>(window, "SettingsTabs");
        var endpoint = Required<TextBox>(window, "CustomEndpointBox");
        endpoint.Text = "http://127.0.0.1:8080/v1/chat/completions";
        try
        {
            window.Show();
            Assert.Equal(5, tabs.Items.Count);
            for (var index = 0; index < tabs.Items.Count; index++)
            {
                tabs.SelectedIndex = index;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var save = Required<Button>(window, "SaveSettingsButton");
                AssertVisibleWithinWindow(window, save);
                Assert.DoesNotContain(save.GetVisualAncestors(), x => x is ScrollViewer);
                Assert.Equal("http://127.0.0.1:8080/v1/chat/completions", endpoint.Text);
                Capture(window, $"settings-{index}-{width}-{language}-{dark}");
            }
            Assert.NotNull(Required<TextBox>(window, "BaiduOcrApiKeyBox"));
            Assert.NotNull(Required<TextBox>(window, "GoogleCloudApiKeyBox"));
            Assert.NotNull(Required<ComboBox>(window, "ManagedModelCombo"));
            var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.S, KeyModifiers = KeyModifiers.Control };
            window.RaiseEvent(key);
            Assert.True(key.Handled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Classic_capture_and_sidebar_fit_the_minimum_window()
    {
        UiText.Configure("zh-CN");
        var window = new MainWindow { Width = 800, Height = 560 };
        try
        {
            window.Show();
            window.UpdateLayout();
            AssertVisibleWithinWindow(window, Required<Button>(window, "CaptureButton"));
            AssertVisibleWithinWindow(window, Required<TabItem>(window, "AppearanceSettingsTab"));
            AssertVisibleWithinWindow(window, Required<Button>(window, "SaveSettingsButton"));
        }
        finally { window.Close(); }
    }

    private static T Required<T>(Window window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control: {name}");

    private static void AssertVisibleWithinWindow(Window window, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.True(control.IsVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        var position = control.TranslatePoint(default, window);
        Assert.True(position.HasValue);
        Assert.InRange(position.Value.X, -1, window.ClientSize.Width);
        Assert.InRange(position.Value.Y, -1, window.ClientSize.Height);
        Assert.True(position.Value.X + control.Bounds.Width <= window.ClientSize.Width + 1);
        Assert.True(position.Value.Y + control.Bounds.Height <= window.ClientSize.Height + 1);
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
}

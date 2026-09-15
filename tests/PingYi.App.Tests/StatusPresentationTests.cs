using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace PingYi.App.Tests;

public class StatusPresentationTests
{
    [AvaloniaFact]
    public void Status_text_keeps_a_visible_theme_brush_after_errors_and_live_theme_changes()
    {
        UiText.Configure(UiText.English);
        var settings = new SettingsWindow { RequestedThemeVariant = ThemeVariant.Light };
        var result = new ResultWindow { RequestedThemeVariant = ThemeVariant.Light };
        try
        {
            settings.Show(); result.Show();
            foreach (var dark in new[] { false, true, false })
            {
                var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                settings.RequestedThemeVariant = theme;
                result.RequestedThemeVariant = theme;
                settings.SetGlobalStatus("Settings saved.", false);
                AssertBrush(settings, "GlobalStatusText", "SecondaryTextBrush");
                settings.SetGlobalStatus("Synthetic save error", true);
                result.SetError("Synthetic processing error");
                AssertBrush(settings, "GlobalStatusText", "DangerTextBrush");
                AssertBrush(result, "StatusText", "DangerTextBrush");
                // Change theme without setting another status; existing bindings must update.
                settings.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
                result.RequestedThemeVariant = settings.RequestedThemeVariant;
                AssertBrush(settings, "GlobalStatusText", "DangerTextBrush");
                AssertBrush(result, "StatusText", "DangerTextBrush");
            }
        }
        finally { settings.Close(); result.ClosePermanently(); }
    }

    [AvaloniaTheory]
    [InlineData("zh-CN", false)]
    [InlineData("en-US", true)]
    public void Mode_picker_default_and_scrolled_previews_use_real_controls(string language, bool dark)
    {
        UiText.Configure(language);
        var window = new ModePickerWindow { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Frame(window, $"fixed-mode-comparison-{language}");
            var scroll = window.FindControl<ScrollViewer>("ModesScroll")!;
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Frame(window, $"fixed-mode-comparison-bottom-{language}");
            Assert.True(scroll.Offset.Y > 0);
        }
        finally { window.Close(); }
    }

    private static void AssertBrush(Window window, string name, string key)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var text = window.FindControl<TextBlock>(name)!;
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground);
        Assert.True(brush.Color.A > 0);
        Assert.True(Application.Current!.TryGetResource(key, window.ActualThemeVariant, out var expected));
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color, brush.Color);
    }
    private static void Frame(Window window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame); frame.Save(Path.Combine(directory, name + ".png"));
    }
}

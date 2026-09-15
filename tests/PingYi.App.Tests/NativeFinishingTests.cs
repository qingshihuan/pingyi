using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using SkiaSharp;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PingYi.App;
using Xunit;

namespace PingYi.App.Tests;

public class NativeFinishingTests
{
    [AvaloniaTheory]
    [InlineData(false, "zh-CN")]
    [InlineData(true, "zh-CN")]
    [InlineData(false, "en-US")]
    [InlineData(true, "en-US")]
    public void Grouped_preferences_fit_default_window_and_keep_inputs(bool dark, string language)
    {
        UiText.Configure(language);
        var window = new MainWindow { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            C<TextBlock>(window, "GlobalStatusText").Text = WorkspaceText.Preview;
            Choice(window, "OcrProviderCombo", "PaddleOCR · ONNX");
            Choice(window, "TranslationProviderCombo", "Argos · Offline");
            Choice(window, "TargetLanguageCombo", UiText.IsEnglish ? "Auto (Chinese / English)" : "自动 · 中英互译");
            window.Show();
            window.UpdateLayout();
            foreach (var name in new[] { "OcrProviderCombo", "TranslationProviderCombo", "TargetLanguageCombo", "SaveSettingsButton" })
                AssertInWindow(window, C<Control>(window, name));
            var check = window.GetVisualDescendants().OfType<Button>()
                .Single(b => AutomationProperties.GetName(b) == UiText.T("检查处理引擎状态"));
            AssertInScrollViewport(check);
            Assert.DoesNotContain(C<Button>(window, "SaveSettingsButton").GetVisualAncestors(), c => c is ScrollViewer);
            C<TextBox>(window, "CustomModelBox").Text = "unsaved-example";
            var tabs = C<TabControl>(window, "SettingsTabs");
            for (var i = 0; i < 5; i++)
            {
                tabs.SelectedIndex = i;
                window.UpdateLayout();
                Assert.Equal("unsaved-example", C<TextBox>(window, "CustomModelBox").Text);
            }
            tabs.SelectedIndex = 0;
            Frame(window, $"polished-settings-{language}-{dark}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(800, 560, "zh-CN")]
    [InlineData(800, 560, "en-US")]
    [InlineData(1040, 720, "zh-CN")]
    [InlineData(1040, 720, "en-US")]
    public void Capture_action_remains_visible_at_both_sizes(int width, int height, string language)
    {
        UiText.Configure(language);
        var window = new MainWindowV2 { Width = width, Height = height };
        try
        {
            C<TextBlock>(window, "LiveStatusTitleText").Text = UiText.IsEnglish ? "UI preview" : "界面预览";
            C<TextBlock>(window, "LiveStatusDetailText").Text = WorkspaceText.Preview;
            window.Show(); window.UpdateLayout();
            AssertInWindow(window, C<Button>(window, "CaptureButtonV2"));
            AssertInWindow(window, C<TextBlock>(window, "LiveStatusTitleText"));
            if (width >= 1040)
            {
                AssertInScrollViewport(C<Button>(window, "RefreshWorkspaceButton"));
                AssertInScrollViewport(C<TextBlock>(window, "ModeRequirementsText"));
            }
            var button = C<Button>(window, "CaptureButtonV2");
            button.IsEnabled = false;
            Assert.False(button.IsEffectivelyEnabled);
            Frame(window, $"polished-busy-{width}-{language}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Resolves_an_available_sans_family_instead_of_a_mixed_asset_uri()
    {
        var installed = SKFontManager.Default.FontFamilies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] preferred = ["Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC",
            "Noto Sans SC", "PingFang SC", "WenQuanYi Micro Hei", "Segoe UI", "DejaVu Sans"];
        var expected = preferred.FirstOrDefault(installed.Contains);
        Assert.Equal(expected ?? "Inter", DesktopTypography.Interface.Name);
    }

    private static void AssertInScrollViewport(Control c)
    {
        var scroll = c.GetVisualAncestors().OfType<ScrollViewer>().First();
        var p = c.TranslatePoint(default, scroll);
        Assert.True(p.HasValue && p.Value.Y >= 0);
        Assert.True(p.Value.Y + c.Bounds.Height <= scroll.Bounds.Height + 1,
            $"{c.Name ?? c.GetType().Name} extends below its scroll viewport.");
    }

    private static T C<T>(Window w, string n) where T : Control => w.FindControl<T>(n) ?? throw new InvalidOperationException(n);
    private static void Choice(Window w, string n, string text)
    { var c = C<ComboBox>(w,n); c.ItemsSource = new[] { text }; c.SelectedIndex = 0; }
    private static void AssertInWindow(Window w, Control c)
    {
        var p = c.TranslatePoint(default, w);
        Assert.True(p.HasValue && c.IsVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0);
        Assert.True(p.Value.X >= 0 && p.Value.Y >= 0);
        Assert.True(p.Value.X + c.Bounds.Width <= w.ClientSize.Width + 1);
        Assert.True(p.Value.Y + c.Bounds.Height <= w.ClientSize.Height + 1);
    }
    private static void Frame(Window w, string name)
    {
        w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var dir = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        using var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame); frame.Save(Path.Combine(dir, name + ".png"));
    }
}

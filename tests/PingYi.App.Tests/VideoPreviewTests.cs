using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

// Authored synthetic previews for documentation, never user captures or live model output.
// Run the existing test assembly with PINGYI_VIDEO_ASSETS set to export the UI frames.
public sealed class VideoPreviewTests
{
    [AvaloniaFact]
    public void Video_frames_use_real_controls_without_network_or_model_downloads()
    {
        UiText.Configure("zh-CN");
        var calls = 0;
        var firstRun = new FirstRunSetupWindow(true,
            (_, _, _, _) => { calls++; return Task.CompletedTask; },
            (_, _) => { calls++; return Task.CompletedTask; }) { RequestedThemeVariant = ThemeVariant.Light };
        var home = new MainWindow { Width = 1040, Height = 720, RequestedThemeVariant = ThemeVariant.Light };
        var modes = new ModePickerWindow(new AppSettings()) { Width = 740, Height = 680, RequestedThemeVariant = ThemeVariant.Light };
        var settings = new SettingsWindow { Width = 1040, Height = 760, RequestedThemeVariant = ThemeVariant.Light };
        var result = new ResultWindow { Width = 780, Height = 640, RequestedThemeVariant = ThemeVariant.Light };
        try
        {
            firstRun.Show();
            Assert.True(firstRun.DownloadButton.IsEnabled);
            Save(firstRun, "first-run");
            Assert.Equal(0, calls);
            firstRun.Hide();
            home.Show();
            // Presentation-only data, explicitly labelled. No provider is invoked.
            Text(home, "ModeSummaryText", "基础模式");
            Text(home, "OcrSummaryText", "本地自定义大模型 OCR");
            Text(home, "TranslationSummaryText", "本地自定义大模型翻译");
            Text(home, "OcrHealthText", "演示：按需启动");
            Text(home, "TranslationHealthText", "演示：按需启动");
            home.RenderRuntimeStatus(RuntimeStatusUiTests.SyntheticSnapshot());
            Text(home, "TopStatusText", "界面演示");
            Text(home, "LiveStatusDetailText", "合成界面预览 · 无真实截图、凭据或模型推理");
            Text(home, "CaptureHotkeyText", "Ctrl Alt D");
            Save(home, "home");
            modes.Show(); Save(modes, "modes");
            settings.Show();
            settings.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 3;
            Save(settings, "custom-service");
            result.Show();
            result.BeginPurpose(CapturePurpose.Auto);
            result.SetAutomaticDecision(new(null, CaptureDecisionReason.MixedContent, 1));
            result.SetLoading("演示：请选择如何处理同一张截图", "合成数据，不进行截图或网络访问");
            Save(result, "manual-choice");
            result.BeginPurpose(CapturePurpose.DecodeQrCode);
            result.SetQrResults([new QrCodeResult("https://github.com/qingshihuan/pingyi")]);
            Save(result, "qr-result");
            Assert.Equal(0, calls);
        }
        finally
        {
            firstRun.Close(); home.Close(); modes.Close(); settings.Close(); result.ClosePermanently();
        }
    }
    private static void Text(Window window, string name, string value) => window.FindControl<TextBlock>(name)!.Text = value;
    private static void Save(Window window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var folder = Environment.GetEnvironmentVariable("PINGYI_VIDEO_ASSETS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(folder, name + ".png"));
    }
}

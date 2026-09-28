using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public sealed class AutomaticCaptureUiTests
{
    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void Automatic_decision_keeps_manual_choices_enabled_and_language_switch_preserves_content(string language)
    {
        UiText.Configure(language);
        var window = new ResultWindow();
        try
        {
            window.Show();
            window.BeginPurpose(CapturePurpose.Auto);
            window.SetAutomaticDecision(new(null, CaptureDecisionReason.MixedContent, 2));
            window.SetLoading(CaptureUiText.Preparing, CaptureUiText.LocalProbePrivacy);
            Assert.Equal(CapturePurpose.Auto, window.Purpose);
            Assert.Equal(CapturePurpose.Auto, window.RequestedPurpose);
            Assert.Equal(CaptureUiText.Automatic, window.FindControl<TextBlock>("ResultHeading")!.Text);
            foreach (var name in new[] { "TranslateResultButton", "DescribeResultButton", "QrResultButton" })
                Assert.True(window.FindControl<Button>(name)!.IsEffectivelyEnabled);
            var requested = new List<CapturePurpose>();
            window.AnalyzeRequested += purpose => { requested.Add(purpose); return Task.CompletedTask; };
            window.FindControl<Button>("QrResultButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { CapturePurpose.DecodeQrCode }, requested);
            window.BeginPurpose(CapturePurpose.DecodeQrCode);
            window.SetQrResults([new QrCodeResult("synthetic payload")]);
            UiText.Configure(language == "zh-CN" ? "en-US" : "zh-CN");
            Assert.Equal("synthetic payload", window.BuildCopyText());
            Assert.Equal(CapturePurpose.DecodeQrCode, window.RequestedPurpose);
            Frame(window, "automatic-manual-" + language);
        }
        finally { window.ClosePermanently(); }
    }

    [AvaloniaTheory]
    [InlineData("zh-CN", false)]
    [InlineData("en-US", true)]
    public async Task First_run_requires_an_explicit_choice_and_lightweight_does_not_download(string language, bool hasRuntime)
    {
        UiText.Configure(language);
        var downloads = 0;
        InitialSetupChoice? saved = null;
        var owner = new Window();
        var dialog = new FirstRunSetupWindow(hasRuntime,
            (_, _, _, _) => { downloads++; return Task.CompletedTask; },
            (choice, _) => { saved = choice; return Task.CompletedTask; });
        try
        {
            owner.Show();
            var selection = dialog.ShowDialog<InitialSetupChoice>(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.False(selection.IsCompleted);
            Assert.Equal(0, downloads);
            Assert.Null(saved);
            Assert.Equal(hasRuntime, dialog.DownloadButton.IsEnabled);
            Assert.True(dialog.LightweightButton.IsEnabled);
            Frame(dialog, "first-run-" + language);
            dialog.LightweightButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await dialog.OperationTask;
            Assert.Equal(InitialSetupChoice.Ready, await selection);
            Assert.Equal(InitialSetupChoice.Ready, saved);
            Assert.Equal(0, downloads);
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task Cancelling_model_download_keeps_setup_open_without_saving_a_choice()
    {
        var owner = new Window();
        var started = false;
        var saves = 0;
        var dialog = new FirstRunSetupWindow(true,
            async (_, _, _, token) => { started = true; await Task.Delay(Timeout.InfiniteTimeSpan, token); },
            (_, _) => { saves++; return Task.CompletedTask; });
        try
        {
            owner.Show();
            var selection = dialog.ShowDialog<InitialSetupChoice>(owner);
            dialog.DownloadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(started);
            Assert.False(dialog.LightweightButton.IsEnabled);
            dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await dialog.OperationTask;
            Assert.False(selection.IsCompleted);
            Assert.True(dialog.LightweightButton.IsEnabled);
            Assert.Equal(0, saves);
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task Model_failure_is_not_reported_as_success_and_can_be_retried()
    {
        var attempts = 0;
        var owner = new Window();
        var dialog = new FirstRunSetupWindow(true, (_, _, _, _) =>
        {
            attempts++;
            return attempts == 1 ? Task.FromException(new IOException("Synthetic model failure")) : Task.CompletedTask;
        }, (_, _) => Task.CompletedTask);
        try
        {
            owner.Show();
            var selection = dialog.ShowDialog<InitialSetupChoice>(owner);
            dialog.DownloadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await dialog.OperationTask;
            Assert.False(selection.IsCompleted);
            Assert.True(dialog.DownloadButton.IsEnabled);
            dialog.DownloadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await dialog.OperationTask;
            Assert.Equal(InitialSetupChoice.Ready, await selection);
            Assert.Equal(2, attempts);
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public void Main_exposes_both_automatic_and_manual_controls()
    {
        UiText.Configure("en-US");
        var window = new MainWindow();
        try
        {
            window.Show();
            var controls = window.GetLogicalDescendants().OfType<Control>().Distinct().ToArray();
            Assert.Contains(controls, c => c.Name == "TranslateCaptureButton");
            var toggle = Assert.Single(controls.OfType<CheckBox>(), c => c.Name == "AutomaticCaptureToggle");
            Assert.True(toggle.IsChecked);
            toggle.IsChecked = false;
            Assert.False(toggle.IsChecked);
            Frame(window, "smart-capture-home");
        }
        finally { window.Close(); }
    }
    private static void Frame(Window window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var path = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(path, name + ".png"));
    }
}

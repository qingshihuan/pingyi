using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public sealed class RemoteCaptureConsentUiTests
{
    [AvaloniaTheory]
    [InlineData("zh-CN", 620, RemoteCaptureConsentChoice.Deny)]
    [InlineData("zh-CN", 440, RemoteCaptureConsentChoice.AllowOnce)]
    [InlineData("zh-CN", 620, RemoteCaptureConsentChoice.AlwaysAllow)]
    [InlineData("en-US", 440, RemoteCaptureConsentChoice.AlwaysAllow)]
    public async Task Automatic_dialog_offers_three_explicit_choices_at_small_sizes(string language, int width, RemoteCaptureConsentChoice choice)
    {
        UiText.Configure(language);
        var owner = new Window();
        var dialog = new AutomaticConsentWindow("Synthetic service · https://example.test · synthetic-vision")
        { Width = width, Height = 370, RequestedThemeVariant = language == "en-US" ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            owner.Show();
            var answer = dialog.ShowDialog<RemoteCaptureConsentChoice>(owner);
            dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.False(answer.IsCompleted);
            Assert.True(dialog.CancelButton.IsFocused);
            foreach (var button in new[] { dialog.CancelButton, dialog.ConfirmButton, dialog.RememberButton })
                AssertWithinWindow(dialog, button);
            Assert.Equal(language == "en-US" ? "Allow and remember" : "同意并不再提示", dialog.RememberButton.Content);
            Frame(dialog, $"automatic-consent-{language}-{width}");
            var selected = choice switch
            {
                RemoteCaptureConsentChoice.AllowOnce => dialog.ConfirmButton,
                RemoteCaptureConsentChoice.AlwaysAllow => dialog.RememberButton,
                _ => dialog.CancelButton
            };
            selected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(choice, await answer);
        }
        finally { dialog.Close(RemoteCaptureConsentChoice.Deny); owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public async Task Image_analysis_remember_button_is_visible_and_returns_persistent_choice(string language)
    {
        UiText.Configure(language);
        var owner = new Window();
        var dialog = new ImageAnalysisConsentWindow { Width = 460, Height = 360,
            RequestedThemeVariant = language == "en-US" ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            owner.Show();
            dialog.FindControl<TextBlock>("DestinationText")!.Text = "https://example.test";
            dialog.FindControl<TextBlock>("ModelText")!.Text = "synthetic-vision";
            var answer = dialog.ShowDialog<RemoteCaptureConsentChoice>(owner);
            dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.FindControl<Button>("RejectButton")!.IsFocused);
            foreach (var name in new[] { "RejectButton", "ConfirmButton", "RememberButton" })
                AssertWithinWindow(dialog, dialog.FindControl<Button>(name)!);
            Frame(dialog, $"analysis-consent-{language}-narrow");
            dialog.FindControl<Button>("RememberButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(RemoteCaptureConsentChoice.AlwaysAllow, await answer);
        }
        finally { dialog.Close(RemoteCaptureConsentChoice.Deny); owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_or_escaping_an_unanswered_dialog_grants_nothing(bool escape)
    {
        var owner = new Window();
        var dialog = new AutomaticConsentWindow("Synthetic destination");
        try
        {
            owner.Show();
            var answer = dialog.ShowDialog<RemoteCaptureConsentChoice>(owner);
            if (escape) dialog.KeyPress(Key.Escape, default, PhysicalKey.Escape, null); else dialog.Close();
            Assert.Equal(RemoteCaptureConsentChoice.Deny, await answer);
        }
        finally { dialog.Close(RemoteCaptureConsentChoice.Deny); owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reset_is_immediate_preserves_drafts_and_reports_write_failure(bool fail)
    {
        UiText.Configure("zh-CN");
        var window = new SettingsWindow();
        var release = new TaskCompletionSource();
        var writes = 0;
        window.ResetCaptureApprovals = async _ => { writes++; await release.Task; if (fail) throw new IOException("Synthetic write failure"); };
        try
        {
            window.Show();
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 4;
            window.ConfigurationCenter.KeyEditor.Text = "synthetic-unsaved-draft";
            var button = window.FindControl<Button>("ResetCaptureApprovalsButton")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            release.SetResult();
            await window.ResetCaptureApprovalsTask;
            // The duplicate click does not replace the pending operation or discard the form.
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, writes);
            Assert.True(button.IsEnabled);
            Assert.Equal("synthetic-unsaved-draft", window.ConfigurationCenter.KeyEditor.Text);
            var status = window.FindControl<TextBlock>("GlobalStatusText")!.Text ?? "";
            Assert.Equal(!fail, status.Contains("已恢复确认提示", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(status));
            if (!fail) Frame(window, "consent-reset-settings-zh-CN");
        }
        finally { release.TrySetResult(); window.Close(); }
    }

    private static void AssertWithinWindow(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        var point = control.TranslatePoint(default, window);
        Assert.NotNull(point);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Assert.True(point.Value.X >= 0 && point.Value.Y >= 0);
        Assert.True(point.Value.X + control.Bounds.Width <= window.ClientSize.Width + 1);
        Assert.True(point.Value.Y + control.Bounds.Height <= window.ClientSize.Height + 1);
    }

    private static void Frame(Window window, string name)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var directory = Environment.GetEnvironmentVariable("PINGYI_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}

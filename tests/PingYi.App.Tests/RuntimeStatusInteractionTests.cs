using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using PingYi.Core;
using Xunit;

namespace PingYi.App.Tests;

public sealed class RuntimeStatusInteractionTests
{
    [AvaloniaFact]
    public void Details_refresh_and_configuration_buttons_are_real_actions_and_preserve_drafts()
    {
        UiText.Configure("en-US");
        var settings = new SettingsWindow();
        try
        {
            settings.Show();
            var endpoint = settings.FindControl<TextBox>("CustomEndpointBox")!;
            endpoint.Text = "http://127.0.0.1:7654/v1";
            settings.SelectRuntimeStatus();
            var page = settings.FindControl<RuntimeStatusView>("RuntimeStatusPage")!;
            Assert.Single(page.GetLogicalDescendants().OfType<ScrollViewer>().Distinct());
            var refreshes = 0;
            page.RefreshRequested += (_, _) => refreshes++;
            page.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, refreshes);
            var manage = page.GetLogicalDescendants().OfType<Button>().Distinct()
                .Single(button => Equals(button.Content, "Manage runtime"));
            manage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, settings.FindControl<TabControl>("SettingsTabs")!.SelectedIndex);
            Assert.Equal("http://127.0.0.1:7654/v1", endpoint.Text);
            settings.SelectRuntimeStatus();
            Assert.Equal(5, settings.FindControl<TabControl>("SettingsTabs")!.SelectedIndex);
        }
        finally { settings.Close(); }
    }

    [AvaloniaFact]
    public void A_refresh_failure_is_not_erased_when_the_busy_indicator_stops()
    {
        UiText.Configure("en-US");
        var page = new RuntimeStatusView();
        var snapshot = RuntimeStatusUiTests.SyntheticSnapshot();
        page.SetSnapshot(snapshot);
        page.SetBusy(true);
        page.SetError();
        page.SetBusy(false);
        Assert.Contains(page.GetLogicalDescendants().OfType<TextBlock>(),
            text => (text.Text ?? "").Contains("Previous results are retained"));
        Assert.Equal(snapshot, page.Snapshot);
    }

    [AvaloniaFact]
    public void Unverified_cloud_configuration_is_not_rendered_as_online_green()
    {
        var cards = new ModeStatusCards();
        cards.SetSnapshot(RuntimeStatusUiTests.SyntheticSnapshot() with
        {
            Cloud = new(ModeReadinessState.Unverified, "cloud-unverified"),
            Basic = new(ModeReadinessState.OnDemand, "basic-on-demand")
        });
        Assert.False(cards.Cards[2].State.IsReady);
        Assert.NotEqual(Color.Parse("#2C9A5E"), ((ISolidColorBrush)cards.Cards[2].Indicator.Background!).Color);
        Assert.True(cards.Cards[1].State.IsReady);
    }
}

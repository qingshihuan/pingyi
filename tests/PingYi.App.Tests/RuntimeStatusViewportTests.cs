using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace PingYi.App.Tests;

public sealed class RuntimeStatusViewportTests
{
    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void The_whole_status_panel_fits_above_the_fixed_footer_at_default_window_size(string language)
    {
        UiText.Configure(language);
        var home = new MainWindow();
        try
        {
            home.Show();
            home.RenderRuntimeStatus(RuntimeStatusUiTests.SyntheticSnapshot());
            home.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1040, home.Width);
            Assert.Equal(720, home.Height);
            var panel = home.FindControl<Border>("ModeStatusBorder")!;
            var viewport = home.GetLogicalDescendants().OfType<ScrollViewer>().Distinct().Single();
            var bottom = panel.TranslatePoint(new Point(0, panel.Bounds.Height), viewport);
            Assert.NotNull(bottom);
            Assert.True(bottom.Value.Y <= viewport.Bounds.Height + 0.5,
                $"Status panel bottom {bottom.Value.Y} is clipped by viewport height {viewport.Bounds.Height}.");
            Assert.True(panel.Bounds.Height > 0);
        }
        finally { home.Close(); }
    }
}

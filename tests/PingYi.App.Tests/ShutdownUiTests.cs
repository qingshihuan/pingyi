using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Xunit;

namespace PingYi.App.Tests;

public sealed class ShutdownUiTests
{
    [AvaloniaTheory]
    [InlineData("zh-CN", "退出并释放资源")]
    [InlineData("en-US", "Quit and release resources")]
    public void Exit_is_visible_localized_and_only_invoked_once(string language, string expected)
    {
        UiText.Configure(language);
        var window = new MainWindow();
        var calls = 0;
        window.ExitRequested = () => { calls++; return Task.CompletedTask; };
        try
        {
            window.Show();
            var button = window.FindControl<Button>("ExitApplicationButton")!;
            Assert.True(button.IsVisible);
            Assert.Equal(expected, button.Content);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, calls);
            Assert.False(button.IsEnabled);
        }
        finally { window.Close(); }
    }
}

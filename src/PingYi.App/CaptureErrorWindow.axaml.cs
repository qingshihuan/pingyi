using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PingYi.App;

public partial class CaptureErrorWindow : Window
{
    private readonly Action? _openSettings;
    public CaptureErrorWindow() : this(string.Empty, null) { }
    public CaptureErrorWindow(string message, Action? openSettings)
    {
        InitializeComponent();
        UiText.Attach(this);
        CaptureErrorText.Text = message;
        _openSettings = openSettings;
    }
    private void Settings_OnClick(object? sender, RoutedEventArgs e) { Close(); _openSettings?.Invoke(); }
    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();
}

using Avalonia.Input;
using Avalonia.Interactivity;

namespace PingYi.App;

public partial class MainWindow
{
    private async void RefreshWorkspaceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !RefreshWorkspaceButton.IsEnabled)
            return;

        RefreshWorkspaceButton.IsEnabled = false;
        try
        {
            LoadSettings();
            await RefreshDashboardAsync();
        }
        finally
        {
            RefreshWorkspaceButton.IsEnabled = true;
        }
    }

    private void Workspace_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            RefreshWorkspaceButton_OnClick(RefreshWorkspaceButton, new RoutedEventArgs());
        }
        else if (e.Key == Key.OemComma && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            OpenSettings();
        }
    }
}

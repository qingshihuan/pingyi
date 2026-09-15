using Avalonia.Input;
using Avalonia.Interactivity;

namespace PingYi.App;

public partial class SettingsWindow
{
    private void Workspace_OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Reuse validation, secure storage, rollback and busy feedback.
        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            if (SaveSettingsButton.IsEnabled)
                SaveButton_OnClick(SaveSettingsButton, new RoutedEventArgs());
        }
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace PingYi.App;

public partial class ImageAnalysisConsentWindow : Window
{
    public ImageAnalysisConsentWindow()
    {
        InitializeComponent();
        UiText.Attach(this);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(false); };
    }

    public static async Task<bool> ConfirmAsync(Window owner, Uri endpoint, string model, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var dialog = new ImageAnalysisConsentWindow();
        dialog.DestinationText.Text = endpoint.GetLeftPart(UriPartial.Authority);
        dialog.ModelText.Text = model;
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        try { return await dialog.ShowDialog<bool>(owner).WaitAsync(token); }
        finally { dialog.Close(false); }
    }
    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(false);
    private void Confirm_OnClick(object? sender, RoutedEventArgs e) => Close(true);
}

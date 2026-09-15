using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PingYi.Core;

namespace PingYi.App;

public partial class ModePickerWindow : Window
{
    public ModePickerWindow() : this(new AppSettings()) { }
    public ModePickerWindow(AppSettings settings)
    {
        InitializeComponent();
        UiText.Attach(this);
        SetChoices(ProcessingModes.Identify(settings));
        UiText.LanguageChanged += LanguageChanged;
        Closed += (_, _) => UiText.LanguageChanged -= LanguageChanged;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(null); } };
    }
    private void SetChoices(string id)
    {
        ModesList.ItemsSource = null;
        ModesList.ItemsSource = ModeChoice.All;
        ModesList.SelectedItem = ModeChoice.All.First(m => m.Id == id);
    }
    private void LanguageChanged(object? sender, EventArgs e) => SetChoices((ModesList.SelectedItem as ModeChoice)?.Id ?? ProcessingModes.Offline);
    private void ModesList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ApplyModeButton is null) return;
        ApplyModeButton.IsEnabled = ModesList.SelectedItem is ModeChoice;
        ApplyModeButton.Content = UiText.T(ModesList.SelectedItem is ModeChoice { Id: ProcessingModes.Custom } ? "打开设置" : "应用方案");
    }
    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);
    private void Apply_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ModesList.SelectedItem is ModeChoice mode) Close(mode.Id);
    }
}

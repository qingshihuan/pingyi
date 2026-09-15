using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace PingYi.App;

public partial class MainWindow
{
    internal Func<string, Task>? PersistLanguageAsync { get; set; }
    internal Task LanguageChangeTask { get; private set; } = Task.CompletedTask;
    private string _savedLanguageChoice = UiText.CurrentLanguage;
    private bool _languageSelectionBusy;

    private void InitializeLanguageSelection()
    {
        UiText.LanguageChanged += WindowLanguageChanged;
        Closed += (_, _) => UiText.LanguageChanged -= WindowLanguageChanged;
        _isLoadingSettings = true;
        UiLanguageCombo.ItemsSource = UiLanguageChoice.Create();
        UiLanguageCombo.SelectedItem = ((IEnumerable<UiLanguageChoice>)UiLanguageCombo.ItemsSource)
            .First(c => c.Id == _savedLanguageChoice);
        _isLoadingSettings = false;
        foreach (var combo in this.GetLogicalDescendants().OfType<ComboBox>())
        {
            // Core model/preset display names also use the requested culture, independently
            // of the ExecutionContext inherited by a later UI callback.
            combo.ItemTemplate = new FuncDataTemplate<object>((item, _) =>
            {
                var previous = CultureInfo.CurrentUICulture;
                try
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(UiText.CurrentLanguage);
                    return new TextBlock { Text = UiText.T(item?.ToString()), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                }
                finally { CultureInfo.CurrentUICulture = previous; }
            });
        }
    }

    private void UiLanguageCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || _languageSelectionBusy || UiLanguageCombo.SelectedItem is not UiLanguageChoice choice)
            return;
        LanguageChangeTask = ApplyLanguageSelectionAsync(choice.Id);
    }

    private async Task ApplyLanguageSelectionAsync(string language)
    {
        var previous = _services?.Settings.UiLanguage ?? _savedLanguageChoice;
        _languageSelectionBusy = true;
        UiLanguageCombo.IsEnabled = false;
        try
        {
            if (PersistLanguageAsync is not null) await PersistLanguageAsync(language);
            _savedLanguageChoice = language;
            UiText.Configure(language);
            SetGlobalStatus(UiText.IsEnglish
                ? "Interface language saved and applied. Other unsaved fields are unchanged."
                : "界面语言已保存并应用；其他未保存的输入保持不变。", false);
        }
        catch (Exception exception)
        {
            _isLoadingSettings = true;
            UiLanguageCombo.SelectedItem = ((IEnumerable<UiLanguageChoice>)UiLanguageCombo.ItemsSource!)
                .First(c => c.Id == previous);
            _isLoadingSettings = false;
            SetGlobalStatus(UiText.Error(exception), true);
        }
        finally
        {
            _languageSelectionBusy = false;
            UiLanguageCombo.IsEnabled = true;
        }
    }

    private void WindowLanguageChanged(object? sender, EventArgs e)
    {
        var loading = _isLoadingSettings;
        _isLoadingSettings = true;
        try
        {
            ConfigureWindowMode();
            foreach (var combo in this.GetLogicalDescendants().OfType<ComboBox>())
            {
                if (combo.ItemsSource is null || ReferenceEquals(combo, UiLanguageCombo)) continue;
                var selected = combo.SelectedItem;
                var items = combo.ItemsSource;
                combo.ItemsSource = null;
                combo.ItemsSource = items;
                combo.SelectedItem = selected;
            }
            var id = (UiLanguageCombo.SelectedItem as UiLanguageChoice)?.Id ?? _savedLanguageChoice;
            var languages = UiLanguageChoice.Create();
            UiLanguageCombo.ItemsSource = languages;
            UiLanguageCombo.SelectedItem = languages.First(c => c.Id == id);
            UpdateManagedModelDescription();
            UpdateManagedRuntimeBackendDescription();
        }
        finally { _isLoadingSettings = loading; }
    }

    private async void HelpButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CaptureVisibilityScope.IsActive) return;
        var window = new HelpWindow(_services?.Settings);
        await DialogPresentation.ShowAsync(window, this);
    }
}

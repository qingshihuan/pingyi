using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class SettingsWindow
{
    private Func<string, Task> _persistLanguage = _ => Task.CompletedTask;
    private string _selectedLanguage = UiText.CurrentLanguage;
    private bool _changingLanguage;
    internal Task LanguageChangeTask { get; private set; } = Task.CompletedTask;

    internal SettingsWindow(Func<string, Task> persistLanguage) : this() =>
        _persistLanguage = persistLanguage;

    private void InitializeLanguageSelection()
    {
        var loading = _isLoadingSettings;
        _isLoadingSettings = true;
        try
        {
            var choices = UiLanguageChoice.Create();
            UiLanguageCombo.ItemsSource = choices;
            UiLanguageCombo.SelectedItem = choices.First(c => c.Id == _selectedLanguage);
        }
        finally { _isLoadingSettings = loading; }
    }

    private async void UiLanguageCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || _changingLanguage || UiLanguageCombo.SelectedItem is not UiLanguageChoice choice)
            return;
        LanguageChangeTask = ChangeLanguageAsync(choice.Id);
        await LanguageChangeTask;
    }

    private async Task ChangeLanguageAsync(string language)
    {
        if (language == _selectedLanguage) return;
        _changingLanguage = true;
        UiLanguageCombo.IsEnabled = false;
        try
        {
            // Persist this one preference before changing the application language.
            // No model/credential refresh: that would overwrite unsaved secret editors.
            await _persistLanguage(language);
            _selectedLanguage = language;
            UiText.Configure(language);
            SetGlobalStatus(UiText.Get("String.LanguageApplied"), false);
        }
        catch (Exception exception)
        {
            InitializeLanguageSelection();
            SetGlobalStatus(UiText.Error(exception), true);
        }
        finally
        {
            UiLanguageCombo.IsEnabled = true;
            _changingLanguage = false;
        }
    }

    private void RefreshWindowTitle() =>
        Title = UiText.IsEnglish
            ? AppEdition.IsComplete ? "PingYi Complete Settings" : "PingYi Settings"
            : $"{AppEdition.ProductName}设置";

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshWindowTitle();
        var loading = _isLoadingSettings;
        _isLoadingSettings = true;
        try
        {
            InitializeLanguageSelection();
            if (_services is not null)
            {
                // Keep the form's current choices and every editor instance. Do not call LoadSettings.
                var ocrId = (OcrProviderCombo.SelectedItem as ProviderChoice)?.Id;
                var translationId = (TranslationProviderCombo.SelectedItem as ProviderChoice)?.Id;
                var target = (TargetLanguageCombo.SelectedItem as LanguageChoice)?.Code ?? LanguageCatalog.AutoOpposite;
                var ocr = _services.Providers.OcrProviders.Select(p => new ProviderChoice(p.Metadata.Id,
                    UiText.ProviderName(p.Metadata.Id, p.Metadata.DisplayName))).ToArray();
                var translation = _services.Providers.TranslationProviders.Select(p => new ProviderChoice(p.Metadata.Id,
                    UiText.ProviderName(p.Metadata.Id, p.Metadata.DisplayName))).ToArray();
                OcrProviderCombo.ItemsSource = ocr;
                OcrProviderCombo.SelectedItem = ocr.FirstOrDefault(p => p.Id == ocrId);
                TranslationProviderCombo.ItemsSource = translation;
                TranslationProviderCombo.SelectedItem = translation.FirstOrDefault(p => p.Id == translationId);
                if (translationId is not null) LoadTargetLanguageChoices(translationId, target);
                UpdateManagedModelDescription();
                UpdateManagedRuntimeBackendDescription();
            }
            RefreshChoiceTemplates();
            foreach (var button in _buttonDefaultContents.Keys.ToArray())
                if (_buttonDefaultContents[button] is string text)
                    _buttonDefaultContents[button] = UiText.TranslateExisting(text);
            if (_deleteModelsDefaultContent is string deleteText)
                _deleteModelsDefaultContent = UiText.TranslateExisting(deleteText);
        }
        finally { _isLoadingSettings = loading; }
    }

    private void RefreshChoiceTemplates()
    {
        // Core catalog records use CurrentUICulture. Freeze their display strings here
        // so a later UI dispatcher turn cannot inherit a stale async culture context.
        foreach (var combo in new[] { LocalServicePresetCombo, ManagedModelCombo, ManagedRuntimeBackendCombo })
        {
            var labels = combo.Items.Cast<object>().ToDictionary(item => item, item => UiText.T(item.ToString()));
            combo.ItemTemplate = new FuncDataTemplate<object>((item, _) => new TextBlock
            {
                Text = item is not null && labels.TryGetValue(item, out var label) ? label : string.Empty,
                TextWrapping = TextWrapping.Wrap
            });
        }
    }
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using PingYi.Core;

namespace PingYi.App;

public partial class ModePickerWindow : Window
{
    private readonly AppSettings _settings;
    private readonly TaskCompletionSource<string?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _result;
    internal string SelectedModeId { get; private set; }

    public ModePickerWindow() : this(new AppSettings()) { }
    public ModePickerWindow(AppSettings settings)
    {
        _settings = settings;
        SelectedModeId = ProcessingModes.Match(settings).Id;
        InitializeComponent();
        RebuildChoices();
        UiText.Attach(this);
        UiText.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => { UiText.LanguageChanged -= OnLanguageChanged; _completion.TrySetResult(_result); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    public Task<string?> SelectAsync(Window owner) { Show(owner); return _completion.Task; }

    internal static string NameFor(string id) => UiText.Get($"String.Mode.{id}.Name");

    private void OnLanguageChanged(object? sender, EventArgs e) => RebuildChoices();

    private void RebuildChoices()
    {
        ModeChoices.Children.Clear();
        foreach (var mode in ProcessingModes.All)
        {
            var body = new StackPanel { Spacing = 5 };
            body.Children.Add(Line(NameFor(mode.Id), "workspace-section"));
            body.Children.Add(Line(UiText.Get($"String.Mode.{mode.Id}.Method"), "workspace-label"));
            body.Children.Add(Line(UiText.Get($"String.Mode.{mode.Id}.Use"), "workspace-help"));
            body.Children.Add(Line(Requirements(mode.Id, _settings), "workspace-help"));
            var radio = new RadioButton
            {
                Name = "Mode_" + mode.Id,
                GroupName = "ProcessingMode",
                Content = body,
                IsChecked = mode.Id == SelectedModeId,
                Classes = { "mode-choice" }
            };
            AutomationProperties.SetName(radio, $"{NameFor(mode.Id)}. {UiText.Get($"String.Mode.{mode.Id}.Method")}. {Requirements(mode.Id, _settings)}");
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true)
                {
                    SelectedModeId = mode.Id;
                    UpdateAction();
                }
            };
            ModeChoices.Children.Add(radio);
        }
        UpdateAction();
        CurrentModeText.Text = $"{UiText.Get("String.CurrentMode")}: {NameFor(ProcessingModes.Match(_settings).Id)}";
        UiText.Attach(this);
    }

    internal static string Requirements(string id, AppSettings settings)
    {
        if (id is "llm" or "vision")
        {
            var local = AppSettings.TryParseChatCompletionsEndpoint(settings.CustomTranslationEndpoint, out var endpoint) && endpoint.IsLoopback;
            var scope = UiText.Get(id == "vision"
                ? local ? "String.LocalVisionData" : "String.RemoteVisionData"
                : local ? "String.LocalLlmData" : "String.RemoteLlmData");
            return $"{UiText.Get($"String.Mode.{id}.Needs")} {scope}";
        }
        return UiText.Get($"String.Mode.{id}.Needs");
    }

    private static TextBlock Line(string text, string style) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Classes = { style }
    };

    private void UpdateAction() => ApplyModeButton.Content = UiText.Get(
        SelectedModeId == "custom" ? "String.ConfigureCombination" : "String.UseMode");
    private void Apply_OnClick(object? sender, RoutedEventArgs e) { _result = SelectedModeId; Close(); }
    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close();
}

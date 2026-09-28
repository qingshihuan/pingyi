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
        InitializeComponent(); RebuildChoices(); UiText.Attach(this);
        UiText.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => { UiText.LanguageChanged -= OnLanguageChanged; _completion.TrySetResult(_result); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
    public Task<string?> SelectAsync(Window owner) { Show(owner); return _completion.Task; }
    internal static string NameFor(string id) => id switch
    {
        "basic" => CaptureUiText.Pick("基础模式 · 本机大模型", "Basic · local model"),
        "lite" or "offline" => CaptureUiText.Pick("轻量模式 · 离线 OCR 与翻译", "Lightweight · offline OCR and translation"),
        _ => UiText.Get($"String.Mode.{id}.Name")
    };
    private static string MethodFor(string id) => id switch
    {
        "basic" => CaptureUiText.Pick("本地自定义大模型 OCR → 本地自定义大模型翻译", "Local custom vision OCR → local custom model translation"),
        "lite" => "PaddleOCR → Argos Translate",
        _ => UiText.Get($"String.Mode.{id}.Method")
    };
    private static string UseFor(string id) => id switch
    {
        "basic" => CaptureUiText.Pick("默认推荐流程；直接看图识别，不再经过 PaddleOCR 纠错。", "Recommended flow: direct visual OCR, without a PaddleOCR correction pass."),
        "lite" => CaptureUiText.Pick("无需大模型，适合低配置设备和离线中英基础处理。", "No large model required; suitable for low-resource devices and offline Chinese/English."),
        _ => UiText.Get($"String.Mode.{id}.Use")
    };
    private void OnLanguageChanged(object? sender, EventArgs e) => RebuildChoices();
    private void RebuildChoices()
    {
        ModeChoices.Children.Clear();
        foreach (var mode in ProcessingModes.All)
        {
            var body = new StackPanel { Spacing = 5 };
            body.Children.Add(Line(NameFor(mode.Id), "workspace-section"));
            body.Children.Add(Line(MethodFor(mode.Id), "workspace-label"));
            body.Children.Add(Line(UseFor(mode.Id), "workspace-help"));
            body.Children.Add(Line(Requirements(mode.Id, _settings), "workspace-help"));
            var radio = new RadioButton
            {
                Name = "Mode_" + mode.Id, GroupName = "ProcessingMode", Content = body,
                IsChecked = mode.Id == SelectedModeId, Classes = { "mode-choice" }
            };
            AutomationProperties.SetName(radio, $"{NameFor(mode.Id)}. {MethodFor(mode.Id)}. {Requirements(mode.Id, _settings)}");
            radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) { SelectedModeId = mode.Id; UpdateAction(); } };
            ModeChoices.Children.Add(radio);
        }
        UpdateAction();
        CurrentModeText.Text = $"{UiText.Get("String.CurrentMode")}: {NameFor(ProcessingModes.Match(_settings).Id)}";
        UiText.Attach(this);
    }
    internal static string Requirements(string id, AppSettings settings)
    {
        if (id == "basic") return CaptureUiText.Pick(
            "需要本机视觉模型及其图像组件。可通过主界面的模型引导一键下载配置；仅接受本机回环端点。",
            "Requires a local vision model and projector. Use Model setup for one-click configuration; only loopback endpoints qualify as Basic.");
        if (id is "lite" or "offline") return CaptureUiText.Pick("使用随安装包提供的中英基础模型，不下载大模型。", "Uses the bundled Chinese/English baseline models; no LLM download.");
        if (id == "llm")
        {
            var local = ProcessingModes.IsLocalEndpoint(settings);
            return $"{UiText.Get("String.Mode.llm.Needs")} {UiText.Get(local ? "String.LocalLlmData" : "String.RemoteLlmData")}";
        }
        return UiText.Get($"String.Mode.{id}.Needs");
    }
    private static TextBlock Line(string text, string style) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { style } };
    private void UpdateAction() => ApplyModeButton.Content = UiText.Get(SelectedModeId == "custom" ? "String.ConfigureCombination" : "String.UseMode");
    private void Apply_OnClick(object? sender, RoutedEventArgs e) { _result = SelectedModeId; Close(); }
    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close();
}

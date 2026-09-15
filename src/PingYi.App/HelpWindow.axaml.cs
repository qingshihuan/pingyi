using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class HelpWindow : Window
{
    private readonly AppSettings _settings;
    public HelpWindow() : this(null) { }
    public HelpWindow(AppSettings? settings)
    {
        _settings = settings ?? new AppSettings();
        InitializeComponent();
        UiText.Attach(this);
        RefreshText();
        UiText.LanguageChanged += LanguageChanged;
        Closed += (_, _) => UiText.LanguageChanged -= LanguageChanged;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
    private void LanguageChanged(object? sender, EventArgs e) => RefreshText();
    private void RefreshText()
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "—";
        VersionText.Text = $"{UiText.T(AppEdition.ProductName)} · {version}";
        ShortcutsText.Text = UiText.IsEnglish
            ? $"{_settings.Hotkey} — Capture screen text\nCtrl+, — Settings · F5 — Refresh status\nCtrl+S — Save settings · Esc — Cancel capture"
            : $"{_settings.Hotkey} — 截图识别与翻译\nCtrl+, — 设置 · F5 — 刷新状态\nCtrl+S — 保存设置 · Esc — 取消框选";
        DataFlowText.Text = PrivacyDescription.Describe(_settings);
    }
    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();
    private void OpenProjectLink_OnClick(object? sender, RoutedEventArgs e)
    {
        var suffix = (sender as Button)?.Tag as string;
        var url = suffix switch
        {
            "readme" => "https://github.com/qingshihuan/pingyi#readme",
            "issues" => "https://github.com/qingshihuan/pingyi/issues",
            "releases" => "https://github.com/qingshihuan/pingyi/releases",
            _ => null
        };
        if (url is null) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception exception) { HelpStatusText.Text = UiText.Error(exception); }
    }
}

internal static class PrivacyDescription
{
    internal static string Describe(AppSettings settings)
    {
        var local = UiText.IsEnglish ? "this device" : "本机";
        string CustomDestination()
        {
            if (!Uri.TryCreate(settings.CustomTranslationEndpoint, UriKind.Absolute, out var endpoint))
                return UiText.IsEnglish ? "an unconfigured endpoint" : "尚未配置的接口";
            // Never expose user-info, query strings, paths or API keys in an informational page.
            return endpoint.IsLoopback ? (UiText.IsEnglish ? "the local model service" : "本机模型服务")
                : $"{(UiText.IsEnglish ? "the remote service" : "远程服务")} ({endpoint.IdnHost})";
        }
        var ocr = settings.OcrProviderId switch
        {
            "local-paddle" => local,
            "local-vlm-ocr" or "local-vlm-corrected" => CustomDestination(),
            "baidu-ocr" => "Baidu",
            "google-vision-ocr" => "Google Cloud",
            _ => UiText.IsEnglish ? "the selected OCR provider" : "所选 OCR 提供商"
        };
        var translation = settings.TranslationProviderId switch
        {
            "local-argos" => local,
            "custom-chat" => CustomDestination(),
            "baidu-translate" => "Baidu",
            "google-translate" => "Google Cloud",
            _ => UiText.IsEnglish ? "the selected translator" : "所选翻译提供商"
        };
        return UiText.IsEnglish
            ? $"Selected image → {ocr}\nRecognized text → {translation}"
            : $"所选截图 → {ocr}\n识别文字 → {translation}";
    }
}

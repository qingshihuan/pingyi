using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Threading;
using PingYi.App.Localization;
using PingYi.Core;

namespace PingYi.App;

public static partial class UiText
{
    public const string Auto = "auto";
    public const string Chinese = "zh-CN";
    public const string English = "en-US";

    private static readonly ConditionalWeakTable<AvaloniaObject, Dictionary<AvaloniaProperty, (string Source, string Rendered)>> AttachedObjects = new();
    private static readonly ConditionalWeakTable<Control, object> AttachedRoots = new();
    private static readonly List<WeakReference<Control>> Roots = [];
    private static ChineseStrings? _chinese;
    private static EnglishStrings? _english;
    private static readonly Dictionary<string, string> ResourceEnglish = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> EnglishSources = new(StringComparer.Ordinal);
    public static event EventHandler? LanguageChanged;
    private static bool _isApplying;

    public static string CurrentLanguage { get; private set; } = Resolve(Auto);
    public static bool IsEnglish => CurrentLanguage == English;

    public static void Configure(string? language)
    {
        Dispatcher.UIThread.VerifyAccess();
        _chinese ??= new ChineseStrings();
        _english ??= new EnglishStrings();
        CurrentLanguage = Resolve(language);
        var culture = CultureInfo.GetCultureInfo(CurrentLanguage);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        foreach (var (source, translated) in EnglishText)
            EnglishSources.TryAdd(translated, source);
        foreach (var (key, source) in _chinese)
        {
            if (source is string zh && _english.TryGetValue(key, out var value) && value is string en)
            {
                ResourceEnglish[zh] = en;
                EnglishSources.TryAdd(en, zh);
            }
        }
        _isApplying = true;
        try
        {
            if (Application.Current is { } application)
                foreach (var (key, value) in CurrentResources)
                    application.Resources[key] = value;
        }
        finally { _isApplying = false; }
        Roots.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in Roots.ToArray())
            if (reference.TryGetTarget(out var root)) LocalizeTree(root);
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    private static IResourceDictionary CurrentResources => IsEnglish
        ? (_english ??= new EnglishStrings())
        : (_chinese ??= new ChineseStrings());

    public static string Get(string key) => CurrentResources.TryGetValue(key, out var value)
        ? value?.ToString() ?? string.Empty : key;

    public static string TranslateExisting(string text) =>
        T(EnglishSources.TryGetValue(text, out var source) ? source : text);

    public static string Resolve(string? language)
    {
        if (language is Chinese or English)
        {
            return language;
        }

        return CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "zh" ? Chinese : English;
    }

    public static string T(string? text)
    {
        if (!IsEnglish || string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        if (ResourceEnglish.TryGetValue(text, out var translated) || EnglishText.TryGetValue(text, out translated))
        {
            return translated;
        }

        return TranslateDynamic(text);
    }

    public static string Error(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!IsEnglish)
        {
            return exception.Message;
        }

        if (exception is ProviderException providerException)
        {
            if (providerException.Code.EndsWith("_timeout", StringComparison.OrdinalIgnoreCase))
            {
                return "The operation timed out. Try a smaller capture area, then retry.";
            }

            if (providerException.Code.Equals("RuntimeError", StringComparison.OrdinalIgnoreCase))
            {
                if (providerException.Message.Contains("不支持", StringComparison.Ordinal))
                {
                    return "The offline translator does not support the selected language pair. Choose a multilingual local model or cloud provider.";
                }

                if (providerException.Message.Contains("尚未安装", StringComparison.Ordinal) ||
                    providerException.Message.Contains("找不到", StringComparison.Ordinal))
                {
                    return "The offline translation model is missing. Open Settings → Local models to repair it.";
                }
            }

            return providerException.Code switch
            {
                "no_text" => "No text was found in the selected area. Select a clearer or larger area and retry.",
                "ocr_models_missing" => "The offline OCR model is unavailable. Open Settings → Local models to repair it.",
                "credentials_missing" => "Credentials are missing. Open Settings and add credentials for the selected provider.",
                "credentials_invalid" => "The saved credentials were rejected. Check them in Settings and retry.",
                "custom_endpoint_invalid" or "vlm_ocr_endpoint_invalid" =>
                    "The OpenAI-compatible endpoint or model name is invalid.",
                "custom_endpoint_insecure_transport" or "vlm_ocr_insecure_transport" =>
                    "Remote custom services must use HTTPS; only loopback endpoints may use HTTP.",
                "managed_runtime_unavailable" =>
                    "The local model could not start. Open Settings → Local multimodal model to repair it.",
                "ocr_unavailable" => "The selected OCR provider is unavailable. Open Settings to repair or switch it.",
                "translation_unsupported" or "translation_fallback_language_unsupported" =>
                    "The offline translator does not support this language pair. Choose a multilingual local model or a cloud provider.",
                "translation_unavailable" or "translation_fallback_unavailable" =>
                    "The selected translator is unavailable. Open Settings to repair or switch it.",
                "translation_primary_and_fallback_failed" =>
                    "Both the selected translator and offline fallback failed. Check the provider and local models in Settings.",
                "custom_translate_http" =>
                    "The compatible translation service rejected the request. Check its endpoint, model, and API key.",
                "custom_translate_schema" =>
                    "The compatible service returned an unsupported response format.",
                "vlm_ocr_image_invalid" => "The captured image is empty. Capture the area again.",
                "vlm_ocr_http" =>
                    "The multimodal service rejected the image request. Confirm that the vision projector is loaded.",
                "vlm_ocr_schema" or "vlm_ocr_empty" =>
                    "The multimodal service did not return usable OCR text. Check model vision support and retry.",
                "engine_timeout" => "The offline translation engine timed out. Retry or repair the local model in Settings.",
                "baidu_ocr_http" or "baidu_ocr_api" or "baidu_translate_api" =>
                    "Baidu Cloud rejected the request. Check the credentials, quota, and network connection.",
                "google_vision_http" or "google_vision_api" or "google_translate_http" or "google_translate_api" =>
                    "Google Cloud rejected the request. Check the API key restrictions, enabled APIs, quota, and network.",
                _ => T(providerException.Message)
            };
        }

        if (exception is HttpRequestException)
        {
            return "The service could not be reached. Check the network or local model endpoint, then retry.";
        }

        if (exception is OperationCanceledException)
        {
            return "The operation was canceled.";
        }

        if (exception.Message.Contains("模型文件尚未完整下载", StringComparison.Ordinal) ||
            exception.Message.Contains("llama.cpp", StringComparison.OrdinalIgnoreCase))
        {
            return "The local model is incomplete or could not start. Open Settings → Local multimodal model to repair it.";
        }

        return T(exception.Message);
    }

    public static string ProviderName(string id, string fallback) => id switch
    {
        "local-paddle" => IsEnglish ? "Local PaddleOCR" : "本地 PaddleOCR",
        "local-vlm-corrected" => IsEnglish ? "PaddleOCR + local LLM correction" : "PaddleOCR + 本机大模型纠错",
        "local-vlm-ocr" => IsEnglish ? "Local multimodal LLM OCR" : "本机多模态大模型 OCR",
        "local-argos" => IsEnglish ? "Local Argos" : "本地 Argos",
        "baidu-ocr" => IsEnglish ? "Baidu Cloud OCR" : "百度云 OCR",
        "baidu-translate" => IsEnglish ? "Baidu Translation" : "百度翻译",
        "google-vision-ocr" => "Google Cloud Vision OCR",
        "google-translate" => "Google Cloud Translation",
        "custom-chat" => IsEnglish ? "Local / custom LLM" : "本地 / 自定义大模型",
        _ => T(fallback)
    };

    public static string LanguageName(string code) => code == LanguageCatalog.AutoOpposite
        ? T("自动翻译")
        : LanguageCatalog.All.FirstOrDefault(language =>
            string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase)) is { } language
            ? IsEnglish ? language.EnglishDisplayName : language.DisplayName
            : code;

    public static void Attach(Control root)
    {
        if (!AttachedRoots.TryGetValue(root, out _))
        {
            AttachedRoots.Add(root, new object());
            Roots.Add(new WeakReference<Control>(root));
            root.AttachedToVisualTree += (_, _) => LocalizeTree(root);
            if (root is TabControl tabs)
                tabs.SelectionChanged += (_, _) => LocalizeTree(root);
        }
        LocalizeTree(root);
    }

    private static void LocalizeTree(Control root)
    {
        ApplyAndAttach(root);
        foreach (var child in root.GetLogicalDescendants().OfType<Control>()
                     .Concat(root.GetVisualDescendants().OfType<Control>()).Distinct())
            ApplyAndAttach(child);
    }

    private static void ApplyAndAttach(Control control)
    {
        if (!AttachedObjects.TryGetValue(control, out _))
        {
            AttachedObjects.Add(control, new());
            control.PropertyChanged += (_, eventArgs) =>
            {
                if (_isApplying) return;
                if (eventArgs.Property == TextBlock.TextProperty ||
                    eventArgs.Property == ContentControl.ContentProperty ||
                    eventArgs.Property == Expander.HeaderProperty ||
                    eventArgs.Property == MenuItem.HeaderProperty ||
                    eventArgs.Property == TextBox.PlaceholderTextProperty ||
                    eventArgs.Property == Window.TitleProperty ||
                    eventArgs.Property == AutomationProperties.NameProperty ||
                    eventArgs.Property == ToolTip.TipProperty)
                    LocalizeControl(control);
            };
        }
        LocalizeControl(control);
    }

    private static void LocalizeControl(Control control)
    {
        if (_isApplying) return;
        _isApplying = true;
        try
        {
            // SetCurrentValue preserves DynamicResource expressions. Never process TextBox.Text:
            // source text, translations, endpoint URLs, model names and keys are user data.
            if (control is Window window)
                Update(control, Window.TitleProperty, window.Title, value => window.SetCurrentValue(Window.TitleProperty, value));
            if (control is TextBlock textBlock)
                Update(control, TextBlock.TextProperty, textBlock.Text, value => textBlock.SetCurrentValue(TextBlock.TextProperty, value));
            if (control is ContentControl contentControl && contentControl.Content is string content)
                Update(control, ContentControl.ContentProperty, content, value => contentControl.SetCurrentValue(ContentControl.ContentProperty, value));
            if (control is Expander expander && expander.Header is string header)
                Update(control, Expander.HeaderProperty, header, value => expander.SetCurrentValue(Expander.HeaderProperty, value));
            if (control is MenuItem item && item.Header is string itemHeader)
                Update(control, MenuItem.HeaderProperty, itemHeader, value => item.SetCurrentValue(MenuItem.HeaderProperty, value));
            if (control is TextBox textBox)
                Update(control, TextBox.PlaceholderTextProperty, textBox.PlaceholderText, value => textBox.SetCurrentValue(TextBox.PlaceholderTextProperty, value));
            Update(control, AutomationProperties.NameProperty, AutomationProperties.GetName(control),
                value => control.SetCurrentValue(AutomationProperties.NameProperty, value));
            if (ToolTip.GetTip(control) is string tip)
                Update(control, ToolTip.TipProperty, tip, value => control.SetCurrentValue(ToolTip.TipProperty, value));
        }
        finally { _isApplying = false; }
    }

    private static void Update(Control control, AvaloniaProperty property, string? value, Action<string> write)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var state = AttachedObjects.GetOrCreateValue(control);
        var source = state.TryGetValue(property, out var previous) && value == previous.Rendered
            ? previous.Source
            : EnglishSources.TryGetValue(value, out var original) ? original : value;
        var rendered = T(source);
        state[property] = (source, rendered);
        if (value != rendered) write(rendered);
    }

    private static string TranslateDynamic(string text)
    {
        var value = text;
        value = RecognizingPattern().Replace(value, match =>
            $"Recognizing with {TranslateToken(match.Groups[1].Value)}…");
        value = RecognizedPattern().Replace(value, match =>
            $"{TranslateToken(match.Groups[1].Value)} recognized · Translating…");
        value = CompletedPattern().Replace(value, match =>
            $"Complete · {TranslateToken(match.Groups[1].Value)}");
        value = ReadyPattern().Replace(value, match =>
            $"{TranslateToken(match.Groups[1].Value)} · {TranslateToken(match.Groups[2].Value)} is ready. Press {match.Groups[3].Value} to capture.");
        value = ReadyCapturePattern().Replace(value, match =>
            $"Ready. Press {match.Groups[1].Value} or select Start capture.");
        value = AppliedPattern().Replace(value, match =>
            $"Applied: {TranslateToken(match.Groups[1].Value)} + {TranslateToken(match.Groups[2].Value)}.");
        value = ModePattern().Replace(value, match =>
            $"Switched to ‘{TranslateToken(match.Groups[1].Value)}’. Checking availability.");
        value = TargetLanguagePattern().Replace(value, match =>
            $"Target language: {TranslateToken(match.Groups[1].Value)}.");
        value = UploadBothPattern().Replace(value, match =>
            $"The selected image is sent to {TranslateToken(match.Groups[1].Value)}; recognized text is sent to {TranslateToken(match.Groups[2].Value)}.");
        value = UploadTextPattern().Replace(value, match =>
            $"The image is recognized locally; only recognized text is sent to {TranslateToken(match.Groups[1].Value)}.");
        value = LoadingModelPattern().Replace(value, match =>
            $"Loading {TranslateToken(match.Groups[1].Value)}…");
        value = LoadingBackendPattern().Replace(value, match =>
            $"Loading the model with the {TranslateToken(match.Groups[1].Value)} backend…");
        value = DownloadingModelPattern().Replace(value, match =>
            $"Downloading {TranslateToken(match.Groups[1].Value)} from ModelScope…");
        value = DownloadingFilePattern().Replace(value, match =>
            $"Downloading {match.Groups[1].Value}");
        value = VerifyingFilePattern().Replace(value, match =>
            $"Verifying {match.Groups[1].Value}…");
        value = ExistingFilePattern().Replace(value, match =>
            $"{match.Groups[1].Value} already exists and passed verification");
        value = RunningBackendPattern().Replace(value, match =>
            $"The local model service is running with {TranslateToken(match.Groups[1].Value)}");
        value = CredentialVisiblePattern().Replace(value, match =>
            $"{TranslateToken(match.Groups[1].Value)} is visible and can be edited or pasted.");
        value = CredentialHiddenPattern().Replace(value, match =>
            $"{TranslateToken(match.Groups[1].Value)} is hidden.");
        value = SettingsSavedPattern().Replace(value, match =>
            $"Settings saved. {TranslateToken(match.Groups[1].Value)}");
        value = InitializationPattern().Replace(value, match =>
            $"Initialization failed: {TranslateToken(match.Groups[1].Value)}");
        value = ErrorPrefixPattern().Replace(value, match =>
            $"{TranslateToken(match.Groups[1].Value)}: {TranslateToken(match.Groups[2].Value)}");
        value = MissingLocalModelPattern().Replace(
            value,
            "The service is connected, but model ‘$1’ was not found. Available: $2.");
        return value;
    }

    private static string TranslateToken(string value)
    {
        if (EnglishText.TryGetValue(value, out var translated))
        {
            return translated;
        }

        var knownValue = value switch
        {
            "OCR 模型" => "OCR model",
            "翻译模型" => "Translation model",
            "OCR 凭据" => "OCR credential",
            "翻译凭据" => "Translation credential",
            "Google OCR 凭据" => "Google OCR credential",
            "Google 翻译凭据" => "Google Translation credential",
            "本地 PaddleOCR ONNX" => "Local PaddleOCR ONNX",
            "本机多模态大模型 OCR" => "Local multimodal LLM OCR",
            "PaddleOCR + 本机大模型纠错" => "PaddleOCR + local LLM correction",
            "本地 / 自定义大模型" => "Local / custom LLM",
            "百度云 OCR" => "Baidu Cloud OCR",
            "百度翻译" => "Baidu Translation",
            _ => null
        };
        if (knownValue is not null)
        {
            return knownValue;
        }

        return ManagedMultimodalModels.All.FirstOrDefault(model =>
                   string.Equals(model.DisplayName, value, StringComparison.Ordinal))?.LocalizedDisplayName
               ?? value;
    }

    [GeneratedRegex("^正在使用 (.+) 识别…$")]
    private static partial Regex RecognizingPattern();
    [GeneratedRegex("^(.+) 已识别 · 正在翻译…$")]
    private static partial Regex RecognizedPattern();
    [GeneratedRegex("^处理完成 · (.+)$")]
    private static partial Regex CompletedPattern();
    [GeneratedRegex("^(.+) · (.+) 已就绪，按 (.+) 开始截图。$")]
    private static partial Regex ReadyPattern();
    [GeneratedRegex("^就绪。按 (.+) 或点击“开始截图”。$")]
    private static partial Regex ReadyCapturePattern();
    [GeneratedRegex(@"^已应用：(.+) \+ (.+)。$")]
    private static partial Regex AppliedPattern();
    [GeneratedRegex("^已切换到“(.+)”，正在检查可用性。$")]
    private static partial Regex ModePattern();
    [GeneratedRegex("^目标语言已切换为：(.+)。$")]
    private static partial Regex TargetLanguagePattern();
    [GeneratedRegex("^所选图片(?:将)?发送给 (.+)；(?:识别)?文字(?:将)?发送给 (.+)[。]?$")]
    private static partial Regex UploadBothPattern();
    [GeneratedRegex("^图片在本地识别；(?:只有)?识别文字(?:会|将)发送给 (.+)[。]?$")]
    private static partial Regex UploadTextPattern();
    [GeneratedRegex("^正在加载 (.+)…$")]
    private static partial Regex LoadingModelPattern();
    [GeneratedRegex("^正在使用 (.+) 后端加载模型…$")]
    private static partial Regex LoadingBackendPattern();
    [GeneratedRegex("^正在从魔搭下载 (.+)…$")]
    private static partial Regex DownloadingModelPattern();
    [GeneratedRegex("^正在下载 (.+)$")]
    private static partial Regex DownloadingFilePattern();
    [GeneratedRegex("^正在校验 (.+)…$")]
    private static partial Regex VerifyingFilePattern();
    [GeneratedRegex("^(.+) 已存在并通过校验$")]
    private static partial Regex ExistingFilePattern();
    [GeneratedRegex("^本机模型服务已通过 (.+) 运行$")]
    private static partial Regex RunningBackendPattern();
    [GeneratedRegex("^(.+) 已显示，可直接编辑或粘贴。$")]
    private static partial Regex CredentialVisiblePattern();
    [GeneratedRegex("^(.+) 已隐藏。$")]
    private static partial Regex CredentialHiddenPattern();
    [GeneratedRegex("^设置已保存。(.*)$")]
    private static partial Regex SettingsSavedPattern();
    [GeneratedRegex("^初始化失败：(.+)$")]
    private static partial Regex InitializationPattern();
    [GeneratedRegex("^(OCR 模型|翻译模型|OCR 凭据|翻译凭据|Google OCR 凭据|Google 翻译凭据)：(.+)$")]
    private static partial Regex ErrorPrefixPattern();
    [GeneratedRegex("^服务已连接，但模型名“(.+)”不存在；当前可用：(.+)。$")]
    private static partial Regex MissingLocalModelPattern();
}

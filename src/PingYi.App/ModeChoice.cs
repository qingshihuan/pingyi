using PingYi.Core;

namespace PingYi.App;

public sealed record ModeChoice(string Id, string ZhTitle, string EnTitle, string Method,
    string ZhPurpose, string EnPurpose, string ZhRequirements, string EnRequirements)
{
    public string Title => UiText.IsEnglish ? EnTitle : ZhTitle;
    public string Purpose => UiText.IsEnglish ? EnPurpose : ZhPurpose;
    public string Requirements => UiText.IsEnglish ? EnRequirements : ZhRequirements;
    public static IReadOnlyList<ModeChoice> All { get; } =
    [
        new(ProcessingModes.Offline, "离线基础", "Offline essentials", "PaddleOCR → Argos",
            "中英互译与日常取词，不需要联网。", "Everyday text extraction and Chinese–English translation, without a network.",
            "需要：随安装包提供的离线基础模型。", "Requires: the baseline offline models included with the installer."),
        new(ProcessingModes.LocalTranslation, "本机模型翻译", "Local model translation", "PaddleOCR → Local LLM",
            "本地识别后由大模型翻译；多语言能力与速度取决于模型。", "Local OCR followed by LLM translation. Language coverage and speed depend on the model.",
            "需要：本机兼容服务与已加载的文字模型。", "Requires: a compatible loopback service with a text model loaded."),
        new(ProcessingModes.VisionCorrection, "视觉校对", "Visual proofreading", "PaddleOCR → Vision LLM → LLM",
            "利用图片校对 OCR 初稿，再翻译；适合尝试改善小字或特殊字体。", "Review the OCR draft against the image, then translate. Useful to try with small or unusual type.",
            "需要：本机视觉模型及视觉投影文件；不保证表格或公式识别。", "Requires: a local vision model and projector. No guarantee for tables or equations."),
        new(ProcessingModes.Baidu, "百度云服务", "Baidu cloud", "Baidu OCR → Baidu Translation",
            "通过百度识别并翻译；将所选截图和识别文字发送到百度。", "Recognize and translate with Baidu. Sends the selected image and recognized text to Baidu.",
            "需要：网络、百度 OCR 和翻译凭据及可用配额。", "Requires: a network, Baidu OCR/Translation credentials and available quota."),
        new(ProcessingModes.Google, "Google 云服务", "Google Cloud", "Cloud Vision → Cloud Translation",
            "通过 Google 识别并翻译；将所选截图和识别文字发送到 Google。", "Recognize and translate with Google. Sends the selected image and recognized text to Google.",
            "需要：网络、已启用对应 API 的 Google 项目密钥与配额。", "Requires: a network, a Google project key, enabled APIs and available quota."),
        new(ProcessingModes.Custom, "自定义组合", "Custom combination", "OCR + Translation",
            "分别选择识别与翻译服务，也可连接远程兼容接口。", "Choose OCR and translation independently, including a remote compatible endpoint.",
            "打开识别与翻译设置；不改动当前方案或接口配置。", "Opens recognition and translation settings without changing your current configuration.")
    ];

    public static ModeChoice For(AppSettings settings) => All.First(m => m.Id == ProcessingModes.Identify(settings));
}

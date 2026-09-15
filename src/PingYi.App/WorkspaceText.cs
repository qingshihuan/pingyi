namespace PingYi.App;

/// <summary>Copy for the workspace; follows the existing startup language choice.</summary>
public static class WorkspaceText
{
    private static string Choose(string zh, string en) => UiText.IsEnglish ? en : zh;
    public static string Workspace => Choose("截图翻译工作台", "Capture workspace");
    public static string CaptureTitle => Choose("框选文字，即刻翻译", "Select text. Read it your way.");
    public static string CaptureHint => Choose("选择屏幕上的任意文字区域，自动识别并翻译。", "Select any text on your screen to recognize and translate it.");
    public static string CaptureSteps => Choose("框选区域  →  自动识别  →  复制原文或译文", "Select a region  →  Recognize  →  Copy text or translation");
    public static string CaptureTip => Choose("截图时按 Esc 取消；结果窗口支持复制和固定。", "Press Esc to cancel a capture. Copy or pin text in the result window.");
    public static string Settings => Choose("设置", "Settings");
    public static string Refresh => Choose("刷新状态", "Refresh status");
    public static string QuickSwitch => Choose("切换方案", "Switch mode");
    public static string General => Choose("识别与翻译", "Recognition & translation");
    public static string Models => Choose("本地模型", "Local models");
    public static string Cloud => Choose("云端服务", "Cloud services");
    public static string Custom => Choose("自定义接口", "Custom endpoint");
    public static string Appearance => Choose("外观与启动", "Appearance & startup");
    public static string GeneralHint => Choose("先选识别方式，再选翻译方式与目标语言。", "Choose recognition, translation, and your target language.");
    public static string Immediate => Choose("此页选项立即生效，无需另行保存。", "Changes on this page apply immediately.");
    public static string SaveHint => Choose("其他配置修改后，请保存并应用。Ctrl+S 可快速保存。", "Save and apply other configuration changes. Shortcut: Ctrl+S.");
    public static string ModelHint => Choose("管理离线识别、离线翻译和完全版的本机大模型。", "Manage offline OCR, translation, and Complete edition local models.");
    public static string CloudHint => Choose("仅在选择相应云端引擎时发送数据。验证凭据会联系服务商。", "Data is sent when its cloud engine is selected. Credential validation contacts the provider.");
    public static string CustomHint => Choose("连接本机服务或兼容接口，分别测试文字与图片能力。", "Connect a local or compatible endpoint; test text and vision separately.");
    public static string AppearanceHint => Choose("调整界面、快捷键和启动行为。", "Adjust your interface, capture shortcut, and startup behavior.");
    public static string PrivacyHint => Choose("数据处理范围随当前方案变化，以此处说明为准。", "Data handling depends on the active mode shown here.");
    public static string NoHistory => Choose("不保存截图与翻译历史", "No screenshot or translation history");
    public static string CleanupHint => Choose("仅清理用户下载的翻译模型；不会删除随软件提供的基础模型。", "Only user-downloaded translation models are removed; bundled models are kept.");
    public static string ClassicHint => Choose("保留完整功能入口，截图与设置可在同一窗口使用。", "Keep all features together, with capture and settings in one window.");
    public static string SecretHint => Choose("点击“显示”后编辑密钥，再保存或验证。密钥沿用系统安全存储。", "Select Reveal to edit a key, then save or validate. Keys use the existing system secure store.");
}

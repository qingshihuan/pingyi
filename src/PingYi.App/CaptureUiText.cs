using PingYi.Core;

namespace PingYi.App;

internal static class CaptureUiText
{
    public static string Pick(string chinese, string english) => UiText.IsEnglish ? english : chinese;
    public static string Automatic => Pick("一键识别", "Smart capture");
    public static string Preparing => Pick("正在本地判断截图内容…", "Checking the capture locally…");
    public static string LocalProbePrivacy => Pick("本地探测 · 不上传截图 · 不自动打开二维码", "Local detection · no upload · QR links never open automatically");
    public static string ChooseTask => Pick("请选择下方任务，无需重新截图。", "Choose a task below; no new capture is needed.");
    public static string NotSent => Pick("已取消，未发送内容。仍可切换任务。", "Cancelled; nothing was sent. You can still choose another task.");
    public static string SettingsChanged => Pick("配置已变化，请重新选择任务。未向新端点发送内容。", "Settings changed. Select a task again; nothing was sent to a new endpoint.");
    public static string Decision(CaptureDecision decision) => decision.Reason switch
    {
        CaptureDecisionReason.DecodedQr => Pick($"自动选择：二维码解析（{decision.QrCount} 个）。可随时改选任务。", $"Selected QR decoding ({decision.QrCount}). You can override this choice."),
        CaptureDecisionReason.ReadableText => Pick("自动选择：文字翻译。源语言和目标语言仍按你的设置处理。", "Selected text translation; your source and target language settings still apply."),
        CaptureDecisionReason.NoReadableText => Pick("未探测到可读文字，尝试图片描述；这不保证图片中没有文字。", "No readable text detected; trying image description. Text may still be present."),
        CaptureDecisionReason.MixedContent => Pick($"同时发现文字与 {decision.QrCount} 个二维码，请选择任务。", $"Text and {decision.QrCount} QR code(s) found. Choose the task you need."),
        CaptureDecisionReason.UncertainText => Pick("文字证据不足或内容混合，请手动选择任务。", "Text evidence is uncertain or mixed. Choose a task manually."),
        _ => Pick("本地探测不可用或超时；没有自动上传，请手动选择。", "Local detection is unavailable or timed out. No automatic upload; choose manually.")
    };
}

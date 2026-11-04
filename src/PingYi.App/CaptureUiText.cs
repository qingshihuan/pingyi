using PingYi.Core;

namespace PingYi.App;

internal static class CaptureUiText
{
    public static string Pick(string chinese, string english) => UiText.IsEnglish ? english : chinese;
    public static string Automatic => Pick("一键识别", "Smart capture");
    public static string Preparing => Pick("正在本地检测二维码…", "Checking for QR codes locally…");
    public static string LocalProbePrivacy => Pick("本地探测 · 不上传截图 · 不自动打开二维码", "Local detection · no upload · QR links never open automatically");
    public static string ChooseTask => Pick("请选择下方任务，无需重新截图。", "Choose a task below; no new capture is needed.");
    public static string NotSent => Pick("已取消，未发送内容。仍可切换任务。", "Cancelled; nothing was sent. You can still choose another task.");
    public static string SettingsChanged => Pick("配置已变化，请重新选择任务。未向新端点发送内容。", "Settings changed. Select a task again; nothing was sent to a new endpoint.");
    public static string Decision(CaptureDecision decision) => decision.Reason switch
    {
        CaptureDecisionReason.DecodedQr => Pick($"自动选择：二维码解析（{decision.QrCount} 个）。可随时改选任务。", $"Selected QR decoding ({decision.QrCount}). You can override this choice."),
        _ => Pick("使用当前模型识字并翻译；也可改选图片描述或提示词反推。", "Use the current model to recognize and translate, or choose description or prompt reconstruction.")
    };
}

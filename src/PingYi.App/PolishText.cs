namespace PingYi.App;

/// <summary>Localized labels for the native finishing pass; no new settings.</summary>
public static class PolishText
{
    private static string Choose(string zh, string en) => UiText.IsEnglish ? en : zh;
    public static string CaptureShortTitle => Choose("截图取词，自在阅读。", "Capture text. Keep reading.");
    public static string CaptureShortHint => Choose("框选屏幕文字，让识别与翻译自然发生。", "Select screen text. Let recognition and translation follow.");
    public static string CaptureCancelHint => Choose("按 Esc 取消框选", "Press Esc to cancel a selection");
    public static string RecognitionGroup => Choose("文字识别", "Text recognition");
    public static string TranslationGroup => Choose("翻译偏好", "Translation preferences");
    public static string DataBoundary => Choose("数据与隐私", "Data & privacy");
}

namespace PingYi.App;

public sealed partial class AppServices
{
    // UI status is independent from model readiness. A healthy OCR provider must not erase
    // a failed global binding and claim that pressing the shortcut will work.
    public Exception? HotkeyRegistrationError { get; internal set; }
}

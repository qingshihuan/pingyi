using Avalonia.Controls;
using PingYi.Core;

namespace PingYi.App;

public readonly record struct CaptureDisplay(PixelRect Bounds, double Scaling)
{
    public static IReadOnlyList<CaptureDisplay> From(Window window) => window.Screens.All
        .Select(screen => new CaptureDisplay(
            new PixelRect(
                screen.Bounds.X,
                screen.Bounds.Y,
                screen.Bounds.Width,
                screen.Bounds.Height),
            screen.Scaling))
        .ToArray();
}

public interface IMainWindowShell
{
    bool IsVisible { get; }

    void Show();

    void Hide();

    void Activate();

    void OpenSettings();

    IReadOnlyList<CaptureDisplay> GetCaptureDisplays();

    void SetGlobalStatus(string message, bool isError);
}

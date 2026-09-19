using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PingYi.App;
using PingYi.Core;
using PingYi.Infrastructure;

internal static class Program
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(12);
    private static string DirectoryPath => Environment.GetEnvironmentVariable("PINGYI_PORTAL_TEST_DIR")!;
    private static int _count;
    [STAThread]
    public static int Main()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(DirectoryPath)) return 2;
        XInitThreads();
        // Real X11 backend, no desktop application lifetime, AppServices, or models.
        AppBuilder.Configure<PingYi.App.App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
        var exit = 0;
        using var stop = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Run(); Console.WriteLine($"PASS: {_count} native Linux assertions"); }
            catch (Exception error) { Console.Error.WriteLine(error); exit = 1; }
            finally { stop.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(stop.Token);
        return exit;
    }
    private static void Check(bool value, string text)
    { if (!value) throw new Exception(text); _count++; Console.WriteLine("PASS " + text); }
    private static void Mode(string mode) => File.WriteAllText(Path.Combine(DirectoryPath, "mode"), mode);
    private static async Task Wait(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(Limit);
        while (!condition()) await Task.Delay(20, deadline.Token);
    }
    private static async Task Run()
    {
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "x11");
        await using var first = GlobalHotkeyServiceFactory.Create();
        await using var second = GlobalHotkeyServiceFactory.Create();
        var key = "Ctrl+Alt+Shift+D";
        var pressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Pressed += (_, _) => pressed.TrySetResult();
        await first.StartAsync(key).WaitAsync(Limit);
        try { await second.StartAsync(key).WaitAsync(Limit); throw new Exception("Conflict was ignored"); }
        catch (ProviderException error) { Check(error.Code == "hotkey_conflict", "real XGrabKey BadAccess reported, process survives"); }
        Check(!((IGlobalHotkeyStatus)second).IsRegistered, "conflict is not shown as registered");
        SendHotkey(withNumLock: true);
        await pressed.Task.WaitAsync(Limit);
        Check(true, "real XTest key reaches hotkey service with Num Lock on");
        await first.StopAsync();
        await second.StartAsync(key).WaitAsync(Limit);
        Check(((IGlobalHotkeyStatus)second).IsRegistered, "released shortcut can be claimed after rollback");
        await second.StopAsync();

        var capture = ScreenCaptureServiceFactory.Create();
        var desktop = await capture.CaptureDesktopAsync().WaitAsync(Limit);
        Check(desktop.Width == 1280 && desktop.Height == 720 && desktop.PngBytes.Length > 0, "real XGetImage produces a desktop frame");
        // Real native window + button handler + native overlay, without model initialization.
        var button = new Button { Content = "Capture", Width = 120, Height = 45 };
        var window = new Window { Content = button, Width = 320, Height = 180 };
        Task<ImageFrame?>? selection = null;
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        button.Click += (_, _) => selection = CaptureWindowScope.RunAsync([window], DesktopCaptureBarrier.WaitAsync,
            token => CaptureSelectionWorkflow.SelectAsync(capture, new SkiaImageCropper(), [new CaptureDisplay(desktop.DesktopBounds, 1)],
                (image, displays, cancel) =>
                {
                    var pending = new CaptureOverlaySession(image, displays, new SkiaImageCropper()).ShowAndSelectAsync(cancel);
                    shown.TrySetResult(); return pending;
                }, token), CancellationToken.None);
        window.Show(); window.UpdateLayout();
        try
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await shown.Task.WaitAsync(Limit);
            Check(!window.IsVisible, "capture button hides its owner before native selection");
            await Task.Delay(250); SendSelection();
            var image = await selection!.WaitAsync(Limit);
            Check(image is { Width: 220, Height: 140 }, "native overlay handles mouse selection and correct crop");
            Check(window.IsVisible, "native owner restored after successful selection");
        }
        finally { window.Close(); }

        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "wayland");
        Check(ScreenCaptureServiceFactory.Create() is IInteractiveScreenCaptureService, "Wayland routes away from XWayland root capture");
        var portal = (IInteractiveScreenCaptureService)ScreenCaptureServiceFactory.Create();
        Mode("success");
        var frame = await portal.CaptureSelectionAsync().WaitAsync(Limit);
        Check(frame is { Width: 64, Height: 40 }, "real GIO D-Bus request receives interactive screenshot response");
        Check(File.Exists(Path.Combine(DirectoryPath, "synthetic.png")), "desktop-owned image not deleted");
        Mode("cancel");
        Check(await portal.CaptureSelectionAsync().WaitAsync(Limit) is null, "system Cancel has no error or second overlay");
        Mode("denied");
        try { await portal.CaptureSelectionAsync().WaitAsync(Limit); throw new Exception("Denial ignored"); }
        catch (ProviderException error) { Check(error.Code == "capture_portal_denied", "permission denial is actionable"); }
        Mode("hang");
        using (var cancel = new CancellationTokenSource(600))
        {
            try { await portal.CaptureSelectionAsync(cancel.Token).WaitAsync(Limit); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { Check(true, "native GIO wait is cancellable"); }
        }
        await Wait(() => File.ReadAllText(Path.Combine(DirectoryPath, "events")).Contains("request-close"));
        Check(true, "cancel closes pending system request");
        Mode("unsupported");
        await using var missing = GlobalHotkeyServiceFactory.Create();
        try { await missing.StartAsync(key).WaitAsync(Limit); throw new Exception("Missing portal ignored"); }
        catch (ProviderException error) { Check(error.Code == "hotkey_wayland", "unsupported shortcut portal produces fallback guidance"); }
        Check(!((IGlobalHotkeyStatus)missing).IsRegistered, "unsupported portal never claims a global shortcut");
        Mode("success");
        await using var keys = GlobalHotkeyServiceFactory.Create();
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        keys.Pressed += (_, _) => activated.TrySetResult();
        await keys.StartAsync(key).WaitAsync(Limit);
        var status = (IGlobalHotkeyStatus)keys;
        Check(status.IsRegistered && status.RegisteredShortcut == "Ctrl+Alt+Shift+K", "actual portal-assigned shortcut replaces preferred key");
        await activated.Task.WaitAsync(Limit);
        Check(true, "portal Activated signal reaches app");
        await Wait(() => status.RegisteredShortcut == "Ctrl+Alt+Shift+L");
        Check(true, "compositor shortcut changes update live status");
        await Wait(() => !status.IsRegistered);
        Check(status.RegistrationError is not null, "session revocation is not left falsely ready");
        await keys.StopAsync().WaitAsync(Limit);
        await Wait(() => File.ReadAllText(Path.Combine(DirectoryPath, "events")).Contains("session-close"));
        Check(true, "portal sessions close on release");
        Check(!File.ReadAllText(Path.Combine(DirectoryPath, "events")).Contains("bad-contract"), "real D-Bus payloads satisfy portal contracts");
        Check(SingleInstanceCoordinator.CommandFromArguments(["--capture"]) == "capture", "custom keyboard command routes to capture");
    }
    private static void SendHotkey(bool withNumLock)
    {
        var display = XOpenDisplay(IntPtr.Zero);
        try
        {
            void Key(string name, bool down) => XTestFakeKeyEvent(display, XKeysymToKeycode(display, XStringToKeysym(name)), down, 0);
            if (withNumLock) { Key("Num_Lock", true); Key("Num_Lock", false); }
            foreach (var name in new[] { "Control_L", "Alt_L", "Shift_L", "d" }) Key(name, true);
            foreach (var name in new[] { "d", "Shift_L", "Alt_L", "Control_L" }) Key(name, false);
            XSync(display, false);
        }
        finally { XCloseDisplay(display); }
    }
    private static void SendSelection()
    {
        var display = XOpenDisplay(IntPtr.Zero);
        try
        {
            XTestFakeMotionEvent(display, -1, 100, 100, 0);
            XTestFakeButtonEvent(display, 1, true, 0);
            XTestFakeMotionEvent(display, -1, 320, 240, 0);
            XTestFakeButtonEvent(display, 1, false, 0);
            XSync(display, false);
        }
        finally { XCloseDisplay(display); }
    }
    [DllImport("libX11.so.6")] private static extern int XInitThreads();
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, [MarshalAs(UnmanagedType.Bool)] bool discard);
    [DllImport("libX11.so.6")] private static extern nuint XStringToKeysym(string name);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, nuint symbol);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(IntPtr display, uint code, [MarshalAs(UnmanagedType.Bool)] bool pressed, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeButtonEvent(IntPtr display, uint button, [MarshalAs(UnmanagedType.Bool)] bool pressed, nuint delay);
}

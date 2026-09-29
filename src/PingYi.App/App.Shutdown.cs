using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.App;

public partial class App
{
    private readonly ShutdownOnce _exitOnce = new();
    internal Task ExitAsync() => _exitOnce.RunAsync(ExitCoreAsync);

    private void RegisterShutdown(IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.ShutdownRequested += (_, _) =>
        {
            // Do not block OS logout or turn a system shutdown into "hide to tray".
            _isExiting = true;
            Observe(ExitAsync());
        };
        desktop.Exit += (_, _) => OwnedProcessScope.TerminateAllAtExit(TimeSpan.FromSeconds(2));
    }

    private async Task ExitCoreAsync()
    {
        _isExiting = true; // Gate tray, IPC, hotkeys and late startup continuations first.
        UiText.LanguageChanged -= RefreshTrayLanguage;
        var exitCode = 0;
        var cleanup = ShutdownWork.RunAllAsync(
            () => _captureCoordinator?.DisposeAsync().AsTask() ?? Task.CompletedTask,
            () => _services?.DisposeAsync().AsTask() ?? Task.CompletedTask);
        try { await cleanup.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (Exception)
        {
            // Cleanup failure must not skip backend termination or expose provider diagnostics.
            exitCode = 1;
            Observe(cleanup);
        }
        finally
        {
            if (!OwnedProcessScope.TerminateAllAtExit(TimeSpan.FromSeconds(4))) exitCode = 1;
            try { _trayIcon?.Dispose(); }
            catch (Exception) { exitCode = 1; }
            finally { _trayIcon = null; }
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown(exitCode);
        }
    }

    private static void Observe(Task task) => _ = task.ContinueWith(
        completed => { _ = completed.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}

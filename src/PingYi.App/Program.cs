using Avalonia;
using System;

namespace PingYi.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using var singleInstance = SingleInstanceCoordinator.Create(
            PingYi.Infrastructure.AppEdition.IsComplete);
        if (!singleInstance.IsPrimary)
        {
            var delivered = singleInstance
                .SendToPrimaryAsync(SingleInstanceCoordinator.CommandFromArguments(args))
                .GetAwaiter()
                .GetResult();
            if (!delivered)
            {
                Environment.ExitCode = 2;
            }
            return;
        }

        singleInstance.StartListening();
        App.SingleInstance = singleInstance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        App.SingleInstance = null;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}

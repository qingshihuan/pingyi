using Avalonia;
using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace PingYi.App;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // The isolated CI desktop has no user input. Emit method names only, never
        // exception messages, paths, screenshots, credentials or request bodies.
        EventHandler<FirstChanceExceptionEventArgs>? diagnostic = null;
        if (Environment.GetEnvironmentVariable("PINGYI_LINUX_NATIVE_TESTS") == "1")
        {
            var remaining = 3;
            diagnostic = (_, e) =>
            {
                if (e.Exception is not NullReferenceException || Interlocked.Decrement(ref remaining) < 0) return;
                Console.Error.WriteLine("Initialization diagnostic: NullReferenceException");
                foreach (var frame in new StackTrace(e.Exception, false).GetFrames().Take(12))
                {
                    var method = frame.GetMethod();
                    Console.Error.WriteLine($"  {method?.DeclaringType?.FullName}.{method?.Name}");
                }
            };
            AppDomain.CurrentDomain.FirstChanceException += diagnostic;
        }
        try
        {
            using var singleInstance = SingleInstanceCoordinator.Create(PingYi.Infrastructure.AppEdition.IsComplete);
            if (!singleInstance.IsPrimary)
            {
                var delivered = singleInstance.SendToPrimaryAsync(SingleInstanceCoordinator.CommandFromArguments(args))
                    .GetAwaiter().GetResult();
                if (!delivered) Environment.ExitCode = 2;
                return;
            }
            singleInstance.StartListening();
            App.SingleInstance = singleInstance;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            App.SingleInstance = null;
            if (diagnostic is not null) AppDomain.CurrentDomain.FirstChanceException -= diagnostic;
        }
    }

    // Avalonia configuration, also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}

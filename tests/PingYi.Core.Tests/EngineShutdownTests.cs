using System.Diagnostics;
using System.Reflection;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class EngineShutdownTests
{
    [Fact]
    public async Task Unresponsive_engine_is_stopped_and_disposal_waiters_complete_together()
    {
        await using var engine = new EngineProcessClient(new AppDataPaths());
        await using var scope = OwnedProcessScope.Start(new ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "-u", "-c", "import time; print('ready',flush=True); time.sleep(120)" }
        });
        using var observer = Process.GetProcessById(scope.Process.Id);
        Assert.Equal("ready", await scope.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        typeof(EngineProcessClient).GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, scope.Process);
        typeof(EngineProcessClient).GetField("_processScope", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, scope);
        var first = engine.DisposeAsync().AsTask();
        var second = engine.DisposeAsync().AsTask();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(12));
        Assert.True(observer.HasExited);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.CallAsync("health"));
    }
}

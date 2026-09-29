using System.Diagnostics;
using System.Reflection;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

internal static class EngineCancellationFixture
{
    internal static async Task AssertInFlightCancellationAndRecoveryAsync()
    {
        await using var client = new EngineProcessClient(new AppDataPaths());
        // A one-millisecond timer racing a fast health request does not establish that
        // cancellation happened in flight. Wait for an actual pipe request handshake instead.
        await using var owner = OwnedProcessScope.Start(new ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "-u", "-c", "import sys,time; sys.stdin.readline(); print('request-started',file=sys.stderr,flush=True); time.sleep(120)" }
        });
        using var observer = Process.GetProcessById(owner.Process.Id);
        _ = observer.SafeHandle;
        typeof(EngineProcessClient).GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, owner.Process);
        typeof(EngineProcessClient).GetField("_processScope", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, owner);
        using var cancellation = new CancellationTokenSource();
        var pending = client.CallAsync("health", cancellationToken: cancellation.Token);
        try
        {
            Assert.Equal("request-started", await owner.Process.StandardError.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(pending.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(observer.HasExited);
            var result = await client.CallAsync("health");
            Assert.True(result.TryGetProperty("paddleocr", out _));
        }
        finally
        {
            cancellation.Cancel();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
        }
    }
}

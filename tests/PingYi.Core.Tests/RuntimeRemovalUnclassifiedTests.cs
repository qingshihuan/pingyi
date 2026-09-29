using System.Diagnostics;
using System.Reflection;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class RuntimeRemovalUnclassifiedTests
{
    [Fact]
    public async Task Live_owned_process_with_unassigned_backend_is_also_protected()
    {
        await using var service = new ManagedModelService(new AppDataPaths());
        await using var owner = OwnedProcessScope.Start(new ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            ArgumentList = { "-u", "-c", "import time; print('ready',flush=True); time.sleep(120)" }
        });
        try
        {
            Assert.Equal("ready", await owner.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            typeof(ManagedModelService).GetField("_ownedProcess", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(service, owner.Process);
            typeof(ManagedModelService).GetField("_ownedProcessScope", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(service, owner);
            var error = await Assert.ThrowsAsync<ProviderException>(() => service.RemoveDownloadedRuntimeAsync("rocm", default));
            Assert.Equal("runtime_in_use", error.Code);
            Assert.False(owner.Process.HasExited);
        }
        finally { await service.StopAsync(); }
    }
}

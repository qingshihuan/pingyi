using System.Diagnostics;
using System.Reflection;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class OwnedProcessScopeTests
{
    private static ProcessStartInfo Fixture(string code) => new(OperatingSystem.IsWindows() ? "python" : "python3")
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
        RedirectStandardOutput = true, RedirectStandardError = true,
        ArgumentList = { "-u", "-c", code }
    };
    private const string WaitCode = "import time; print('ready', flush=True); time.sleep(120)";

    [Fact]
    public async Task Stop_waits_for_the_owned_backend_and_leaves_unowned_services_alive()
    {
        using var external = Process.Start(Fixture(WaitCode))!;
        await using var owned = OwnedProcessScope.Start(Fixture(WaitCode));
        using var observer = Process.GetProcessById(owned.Process.Id);
        try
        {
            Assert.Equal("ready", await owned.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("ready", await external.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            await Task.WhenAll(owned.StopAsync(), owned.StopAsync());
            Assert.True(observer.HasExited);
            Assert.False(external.HasExited);
            await owned.StopAsync();
        }
        finally { if (!external.HasExited) external.Kill(true); await external.WaitForExitAsync(); }
    }

    [Fact]
    public async Task Managed_service_disposal_stops_a_loading_child_without_any_model_files()
    {
        await using var managed = new ManagedModelService(new AppDataPaths());
        var scope = OwnedProcessScope.Start(Fixture(WaitCode));
        using var observer = Process.GetProcessById(scope.Process.Id);
        try
        {
            typeof(ManagedModelService).GetField("_ownedProcess", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(managed, scope.Process);
            typeof(ManagedModelService).GetField("_ownedProcessScope", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(managed, scope);
            await Task.WhenAll(managed.DisposeAsync().AsTask(), managed.DisposeAsync().AsTask());
            Assert.True(observer.HasExited);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => managed.EnsureStartedAsync(PingYi.Core.ManagedMultimodalModels.Recommended));
        }
        finally { await scope.StopAsync(); }
    }

    [Fact]
    public async Task Live_descendants_are_terminated_with_the_owned_backend()
    {
        const string tree = "import subprocess,sys,time; sys.stdin.readline(); p=subprocess.Popen([sys.executable,'-c','import time;time.sleep(120)']); print(p.pid,flush=True); time.sleep(120)";
        await using var owned = OwnedProcessScope.Start(Fixture(tree));
        await owned.Process.StandardInput.WriteLineAsync("start");
        await owned.Process.StandardInput.FlushAsync();
        var childId = int.Parse((await owned.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!);
        using var child = Process.GetProcessById(childId);
        try
        {
            await owned.StopAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(child.HasExited);
        }
        finally { if (!child.HasExited) child.Kill(true); }
    }

    [Fact]
    public async Task Windows_job_closes_children_even_after_the_launcher_exits()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string launcher = "import subprocess,sys; sys.stdin.readline(); p=subprocess.Popen([sys.executable,'-c','import time;time.sleep(120)']); print(p.pid,flush=True)";
        await using var owned = OwnedProcessScope.Start(Fixture(launcher));
        Assert.True(owned.HasWindowsJob);
        await owned.Process.StandardInput.WriteLineAsync("start");
        await owned.Process.StandardInput.FlushAsync();
        var childId = int.Parse((await owned.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!);
        using var child = Process.GetProcessById(childId);
        try
        {
            await owned.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(child.HasExited);
            await owned.StopAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(child.HasExited);
        }
        finally { if (!child.HasExited) child.Kill(true); }
    }

    [Fact]
    public void Shell_launched_processes_are_not_adopted() => Assert.Throws<ArgumentException>(() =>
        OwnedProcessScope.Start(new ProcessStartInfo("not-started") { UseShellExecute = true }));
}

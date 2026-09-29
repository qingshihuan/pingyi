using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class RuntimeRemovalTests
{
    [Fact]
    public async Task Uninstall_removes_only_downloaded_backend_and_unshared_archives()
    {
        using var layout = new Layout();
        await using var manager = new RuntimeManager(layout.Store, layout.Bundled);
        var own = new string('a', 64); var shared = new string('b', 64);
        layout.Package("rocm", own, shared); layout.Package("cuda12", shared);
        var result = await manager.RemoveDownloadedAsync("rocm", default);
        Assert.True(result.Removed); Assert.False(result.CleanupPending);
        Assert.False(manager.HasDownloadedBackend("rocm"));
        Assert.Null(manager.Find("rocm"));
        Assert.True(File.Exists(layout.Archive(shared)));
        Assert.False(File.Exists(layout.Archive(own)));
        Assert.NotNull(manager.Find("cuda12"));
        Assert.True(File.Exists(layout.Model));
        Assert.Equal("bundled", manager.Find("vulkan")!.Tag);
    }

    [Fact]
    public async Task Removing_downloaded_Vulkan_reveals_bundled_recovery_without_deleting_it()
    {
        using var layout = new Layout();
        await using var manager = new RuntimeManager(layout.Store, layout.Bundled);
        layout.Package("vulkan", new string('c', 64));
        Assert.NotEqual("bundled", manager.Find("vulkan")!.Tag);
        await manager.RemoveDownloadedAsync("vulkan", default);
        Assert.Equal("bundled", manager.Find("vulkan")!.Tag);
        Assert.False((await manager.RemoveDownloadedAsync("vulkan", default)).Removed);
        Assert.NotNull(manager.Find("cpu"));
    }

    [Theory]
    [InlineData("../models")]
    [InlineData("auto")]
    [InlineData("/tmp")]
    [InlineData("")]
    public async Task Invalid_backend_cannot_escape_its_owned_directory(string backend)
    {
        using var layout = new Layout();
        await using var manager = new RuntimeManager(layout.Store, layout.Bundled);
        await Assert.ThrowsAsync<ArgumentException>(() => manager.RemoveDownloadedAsync(backend, default));
        Assert.True(File.Exists(layout.Model));
    }

    [Fact]
    public async Task Cancellation_before_uninstall_leaves_all_files_and_metadata()
    {
        using var layout = new Layout();
        await using var manager = new RuntimeManager(layout.Store, layout.Bundled);
        layout.Package("rocm", new string('d', 64));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.RemoveDownloadedAsync("rocm", cancelled.Token));
        Assert.NotNull(manager.Find("rocm"));
    }

    [Fact]
    public async Task A_concurrent_install_lock_prevents_uninstallation()
    {
        using var layout = new Layout();
        await using var manager = new RuntimeManager(layout.Store, layout.Bundled);
        layout.Package("rocm", new string('e', 64));
        using var locked = new FileStream(Path.Combine(layout.Store, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => manager.RemoveDownloadedAsync("rocm", default));
        Assert.NotNull(manager.Find("rocm"));
    }

    [Fact]
    public async Task Symlinked_backend_is_never_recursively_removed()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var layout = new Layout();
        await using var manager = new RuntimeManager(layout.Store, layout.Bundled);
        Directory.CreateDirectory(layout.Store);
        var link = Path.Combine(layout.Store, "rocm");
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(layout.Model)!);
        await Assert.ThrowsAsync<IOException>(() => manager.RemoveDownloadedAsync("rocm", default));
        Assert.True(File.Exists(layout.Model));
        Directory.Delete(link);
    }

    [Fact]
    public async Task Active_owned_backend_cannot_be_uninstalled_or_killed_by_uninstall()
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
            Field("_ownedProcess", owner.Process); Field("_ownedProcessScope", owner); Field("_runningBackendId", "rocm");
            var error = await Assert.ThrowsAsync<ProviderException>(() => service.RemoveDownloadedRuntimeAsync("rocm", default));
            Assert.Equal("runtime_in_use", error.Code);
            Assert.False(owner.Process.HasExited);
        }
        finally { await service.StopAsync(); }
        void Field(string name, object value) => typeof(ManagedModelService).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(service, value);
    }

    private sealed class Layout : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "pingyi-remove-" + Guid.NewGuid().ToString("N"));
        public string Store => Path.Combine(_root, "runtime-packages");
        public string Bundled => Path.Combine(_root, "installation", "llama-runtime");
        public string Model => Path.Combine(_root, "models", "keep.gguf");
        private static string Server => OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server";
        public Layout()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Model)!); File.WriteAllText(Model, "synthetic, not model weights");
            foreach (var backend in new[] { "vulkan", "cpu" })
            {
                var directory = Path.Combine(Bundled, backend); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, Server), "bundled fixture");
            }
        }
        public string Archive(string hash) => Path.Combine(Store, "downloads", hash + ".archive");
        public void Package(string backend, params string[] hashes)
        {
            var directory = Path.Combine(Store, backend);
            var version = "b11146-0123456789ab";
            Directory.CreateDirectory(Path.Combine(directory, version));
            File.WriteAllText(Path.Combine(directory, version, Server), "downloaded fixture");
            File.WriteAllText(Path.Combine(directory, "current.json"), JsonSerializer.Serialize(
                new RuntimeInstallation(backend, version, Server, hashes), RuntimeJsonContext.Default.RuntimeInstallation));
            Directory.CreateDirectory(Path.Combine(Store, "downloads"));
            foreach (var hash in hashes) File.WriteAllText(Archive(hash), "cached fixture");
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}

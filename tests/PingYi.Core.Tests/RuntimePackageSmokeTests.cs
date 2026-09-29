using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

/// <summary>Explicit network smoke tests, never run downloads during ordinary unit tests.</summary>
public sealed class RuntimePackageSmokeTests
{
    [Fact]
    public async Task Official_cpu_package_download_extract_launch_and_reuse()
    {
        if (Environment.GetEnvironmentVariable("PINGYI_RUNTIME_PACKAGE_SMOKE") != "1") return;
        var root = Path.Combine(Path.GetTempPath(), "pingyi-runtime-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            await using var manager = new RuntimeManager(Path.Combine(root, "runtime"), Path.Combine(root, "no-bundled"));
            var bundle = RuntimeCatalog.Pinned("cpu", OperatingSystem.IsWindows());
            var installed = await manager.InstallBundleAsync(bundle, false, [], null, timeout.Token);
            Assert.Equal("cpu", installed.Backend);
            Assert.Equal(bundle.Tag, installed.Tag);
            Assert.True(File.Exists(installed.Executable));
            Assert.Equal(installed, manager.Find("cpu"));
            var version = await RuntimeProcessProbe.RunAsync(installed.Executable, ["--version"], TimeSpan.FromSeconds(20), timeout.Token, true);
            Assert.Equal(0, version.ExitCode);
            Assert.Contains("version", (version.Output + version.Error).ToLowerInvariant());
            // Reuse uses the verified archive cache and compares the complete installed file set.
            Assert.Equal(installed, await manager.InstallBundleAsync(bundle, false, [], null, timeout.Token));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

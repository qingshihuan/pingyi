using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class RuntimeMetadataRecoveryTests
{
    [Theory]
    [InlineData("{")]
    [InlineData("{\"Backend\":\"cpu\",\"Tag\":\"b11146-aaaaaaaaaaaa\",\"RelativeExecutable\":\"../../escape\"}")]
    [InlineData("{\"Backend\":\"cpu\",\"Tag\":\"b11146-aaaaaaaaaaaa\",\"RelativeExecutable\":null}")]
    [InlineData("{\"Backend\":\"cpu\",\"Tag\":null,\"RelativeExecutable\":\"llama-server\"}")]
    public async Task Damaged_download_metadata_does_not_break_bundled_fallback(string json)
    {
        var root = Path.Combine(Path.GetTempPath(), "pingyi-runtime-metadata-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = Path.Combine(root, "downloaded"); var bundled = Path.Combine(root, "bundled");
            Directory.CreateDirectory(Path.Combine(store, "cpu"));
            Directory.CreateDirectory(Path.Combine(bundled, "cpu"));
            var executable = Path.Combine(bundled, "cpu", OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server");
            // Lookup-only test; this synthetic placeholder is never executed.
            await File.WriteAllTextAsync(executable, "not an executable");
            await File.WriteAllTextAsync(Path.Combine(store, "cpu", "current.json"), json);
            await using var manager = new RuntimeManager(store, bundled);
            Assert.Equal(new InstalledRuntime("cpu", "bundled", executable), manager.Find("cpu"));
            Assert.Null(manager.Find("unknown"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

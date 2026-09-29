using System.Text.Json;
using System.Text.RegularExpressions;
using PingYi.Core;

namespace PingYi.Infrastructure;

public sealed record RuntimeRemovalResult(bool Removed, bool CleanupPending);

public sealed partial class RuntimeManager
{
    public bool HasDownloadedBackend(string backend) => IsConcreteBackend(backend) &&
        (Directory.Exists(Path.Combine(_store, backend)) || (Directory.Exists(_store) &&
         Directory.EnumerateDirectories(_store, ".removed-" + backend + "-*").Any()));

    internal static bool IsConcreteBackend(string backend) => backend is "cpu" or "vulkan" or "cuda12" or "cuda13" or "rocm";

    public async Task ValidateInstalledAsync(InstalledRuntime runtime, string selection, CancellationToken token)
    {
        var version = await RuntimeProcessProbe.RunAsync(runtime.Executable, ["--version"], TimeSpan.FromSeconds(20), token, true);
        if (version.ExitCode != 0)
            throw new ProviderException("runtime_dependency_missing", "新后端无法启动；旧后端未停止。 / New backend cannot start; the previous backend was not stopped.");
        if (runtime.Backend == "cpu") return;
        var devices = await GpuInventory.ListDevicesAsync(runtime.Executable, runtime.Backend, token);
        if (RuntimeDeviceChoice.Resolve(selection, runtime.Backend, devices) is null)
            throw new ProviderException("runtime_no_gpu", "新后端没有可用显卡；旧后端未停止。 / No GPU in the new backend; the previous backend was not stopped.");
    }

    /// <summary>Only the owned model service may call this after excluding active users.</summary>
    internal async Task<RuntimeRemovalResult> RemoveDownloadedAsync(string backend, CancellationToken token)
    {
        if (!IsConcreteBackend(backend)) throw new ArgumentException("Select one concrete backend.", nameof(backend));
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _installGate.WaitAsync(operation.Token);
        try
        {
            if (!Directory.Exists(_store)) return new(false, false);
            RefuseLinks(_store, false);
            await using var fileLock = new FileStream(Path.Combine(_store, "install.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            var directory = Path.Combine(_store, backend);
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removed = Directory.Exists(directory);
            if (removed)
            {
                RefuseLinks(directory, true);
                ReadHashes(directory, hashes);
                operation.Token.ThrowIfCancellationRequested();
                // Detach metadata and all versions together. Never delete from the installation
                // directory: bundled Vulkan/CPU are recovery components, not downloaded packages.
                Directory.Move(directory, Path.Combine(_store, ".removed-" + backend + "-" + Guid.NewGuid().ToString("N")));
            }
            // No cancellation after detaching: finish bounded local cleanup, reporting locked
            // files honestly instead of claiming their disk space was already reclaimed.
            var pending = false;
            foreach (var detached in Directory.EnumerateDirectories(_store, ".removed-" + backend + "-*"))
            {
                if (!Regex.IsMatch(Path.GetFileName(detached), "^\\.removed-" + backend + "-[a-f0-9]{32}$")) continue;
                try
                {
                    RefuseLinks(detached, true);
                    ReadHashes(detached, hashes);
                    Directory.Delete(detached, true);
                    removed = true;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { pending = true; }
            }
            // Archives are content-addressed. Delete only known hashes with no references in
            // another backend. Unknown metadata means retain caches, never guess ownership.
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var known = true;
            foreach (var other in ManagedRuntimeBackends.All.Where(b => IsConcreteBackend(b.Id) && b.Id != backend))
                known &= ReadHashes(Path.Combine(_store, other.Id), referenced);
            var cache = Path.Combine(_store, "downloads");
            if (known && !pending && Directory.Exists(cache))
            {
                RefuseLinks(cache, false);
                foreach (var hash in hashes.Except(referenced, StringComparer.OrdinalIgnoreCase))
                foreach (var suffix in new[] { ".archive", ".archive.partial" })
                {
                    var file = Path.Combine(cache, hash.ToLowerInvariant() + suffix);
                    if (!File.Exists(file)) continue;
                    RefuseLinks(file, false);
                    try { File.Delete(file); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { pending = true; }
                }
            }
            return new(removed, pending);
        }
        finally { _installGate.Release(); }
    }

    private static bool ReadHashes(string directory, ISet<string> hashes)
    {
        if (!Directory.Exists(directory)) return true;
        var valid = true;
        foreach (var name in new[] { "current.json", "previous.json" })
        {
            var file = Path.Combine(directory, name);
            if (!File.Exists(file)) continue;
            try
            {
                RefuseLinks(directory, false); RefuseLinks(file, false);
                if (new FileInfo(file).Length > 32768) { valid = false; continue; }
                var entry = JsonSerializer.Deserialize(File.ReadAllText(file), RuntimeJsonContext.Default.RuntimeInstallation);
                if (entry?.SourceSha256 is null) { valid = false; continue; }
                foreach (var hash in entry.SourceSha256)
                {
                    if (hash is null || !Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$")) { valid = false; continue; }
                    hashes.Add(hash);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { valid = false; }
        }
        return valid;
    }

    private static void RefuseLinks(string path, bool recursive)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("拒绝清理包含符号链接／目录联接的运行包。 / Refusing to remove a linked runtime path.");
        if (!recursive || (attributes & FileAttributes.Directory) == 0) return;
        foreach (var child in Directory.EnumerateFileSystemEntries(path)) RefuseLinks(child, true);
    }
}

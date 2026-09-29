using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PingYi.Core;

namespace PingYi.Infrastructure;

public sealed record InstalledRuntime(string Backend, string Tag, string Executable);
internal sealed record RuntimeInstallation(string Backend, string Tag, string RelativeExecutable, string[] SourceSha256);

[JsonSerializable(typeof(RuntimeInstallation))]
internal partial class RuntimeJsonContext : JsonSerializerContext { }

public sealed class RuntimeManager : IAsyncDisposable
{
    private readonly string _store;
    private readonly string _bundled;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _installGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task<IReadOnlyList<GpuHardware>>? _hardware;
    private int _disposed;
    public RuntimeManager(AppDataPaths paths) : this(Path.Combine(paths.DataDirectory, "runtime-packages"), paths.LlamaRuntimeDirectory) { }
    internal RuntimeManager(string store, string bundled)
    {
        _store = store; _bundled = bundled;
        _http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(8) })
            { Timeout = Timeout.InfiniteTimeSpan };
    }
    public string DirectoryPath => _store;
    public Task<IReadOnlyList<GpuHardware>> DetectAsync(bool refresh, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_lifetime)
        {
            if (refresh || _hardware is null || _hardware.IsFaulted || _hardware.IsCanceled)
                _hardware = new GpuInventory().DetectAsync(_lifetime.Token);
            return _hardware.WaitAsync(token);
        }
    }

    public InstalledRuntime? Find(string backend)
    {
        if (backend is not ("cpu" or "vulkan" or "cuda12" or "cuda13" or "rocm")) return null;
        var metadata = Path.Combine(_store, backend, "current.json");
        try
        {
            if (File.Exists(metadata) && new FileInfo(metadata).Length <= 32768)
            {
                var entry = JsonSerializer.Deserialize(File.ReadAllText(metadata), RuntimeJsonContext.Default.RuntimeInstallation);
                if (entry is not null && entry.Backend == backend &&
                    !string.IsNullOrWhiteSpace(entry.Tag) && !string.IsNullOrWhiteSpace(entry.RelativeExecutable) &&
                    System.Text.RegularExpressions.Regex.IsMatch(entry.Tag, "^b[0-9]{4,9}-[a-f0-9]{12}$"))
                {
                    var root = Path.Combine(_store, backend, entry.Tag);
                    var executable = SafeRuntimeArchive.ResolvePath(root, entry.RelativeExecutable);
                    if (File.Exists(executable)) return new InstalledRuntime(backend, entry.Tag.Split('-')[0], executable);
                }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException) { }
        if (backend is "cpu" or "vulkan")
        {
            var executable = Path.Combine(_bundled, backend, OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server");
            if (File.Exists(executable)) return new InstalledRuntime(backend, "bundled", executable);
        }
        return null;
    }

    public async Task<InstalledRuntime?> RecommendedInstalledAsync(string requested, string selection, CancellationToken token)
    {
        var hardware = requested == "auto" ? await DetectAsync(false, token) : [];
        return RuntimeHardwarePolicy.Candidates(requested, selection, hardware, OperatingSystem.IsWindows())
            .Select(Find).FirstOrDefault(value => value is not null);
    }

    /// <summary>Explicit download/update action. Normal inference never enters this method.</summary>
    public async Task<InstalledRuntime> InstallAsync(string requested, string selection, bool allowRelays,
        string[] customRelays, IProgress<ManagedModelProgress>? progress, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        token = operation.Token;
        await _installGate.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(_store);
            await using var installLock = new FileStream(Path.Combine(_store, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var hardware = await DetectAsync(false, token);
            var candidates = RuntimeHardwarePolicy.Candidates(requested, selection, hardware, OperatingSystem.IsWindows());
            foreach (var backend in candidates)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (requested == "auto" && (backend is "vulkan" or "cpu") && Find(backend) is { } fallback)
                    {
                        progress?.Report(new ManagedModelProgress("runtime-fallback", $"使用已安装的 {backend} 后端；专用后端未启用或不可用。", 0, 0, true));
                        return fallback;
                    }
                    var bundle = await new RuntimeCatalog(_http).ResolveAsync(backend, OperatingSystem.IsWindows(), token);
                    progress?.Report(new ManagedModelProgress("runtime-catalog", bundle.IsPinnedFallback
                        ? $"官方版本查询不可用，使用内置可信清单 {bundle.Tag}，不宣称为最新。"
                        : $"官方版本：{bundle.Tag} · {backend} · {bundle.DownloadBytes / 1048576d:0} MiB", 0, bundle.DownloadBytes, true));
                    return await InstallBundleAsync(bundle, allowRelays, customRelays, progress, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) when (requested == "auto" && selection == "auto" &&
                    error is ProviderException or IOException or InvalidDataException or HttpRequestException or System.ComponentModel.Win32Exception or OperationCanceledException)
                {
                    progress?.Report(new ManagedModelProgress("runtime-fallback", $"{backend} 不可用，检查下一兼容后端；未修改系统驱动。", 0, 0, true));
                }
            }
            throw new ProviderException("runtime_install_failed", "没有可用的运行后端，请检查驱动、启用备用源，或选择内置 Vulkan／CPU。");
        }
        finally { _installGate.Release(); }
    }

    internal async Task<InstalledRuntime> InstallBundleAsync(RuntimeBundle bundle, bool relays, string[] customRelays,
        IProgress<ManagedModelProgress>? progress, CancellationToken token)
    {
        var baseDirectory = Path.Combine(_store, bundle.Backend);
        Directory.CreateDirectory(baseDirectory);
        var signature = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('|', bundle.Assets.Select(a => a.Sha256))))).ToLowerInvariant()[..12];
        var directoryName = bundle.Tag + "-" + signature;
        var target = Path.Combine(baseDirectory, directoryName);
        var stage = Path.Combine(baseDirectory, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var disk = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(stage))!);
            if (disk.AvailableFreeSpace < bundle.DownloadBytes * 5 + 256 * 1048576L)
                throw new IOException("磁盘空间不足，运行包解压需要额外临时空间。");
            for (var index = 0; index < bundle.Assets.Length; index++)
            {
                var asset = bundle.Assets[index];
                var archive = Path.Combine(_store, "downloads", asset.Sha256.ToLowerInvariant() + ".archive");
                await new RuntimeDownloader(_http).DownloadAsync(asset, archive, relays, progress, token, customRelays);
                progress?.Report(new ManagedModelProgress("runtime-extract", $"校验通过，正在解压 {asset.Name}…", index, bundle.Assets.Length, true));
                await SafeRuntimeArchive.ExtractAsync(archive, asset.Name, Path.Combine(stage, index.ToString()), token);
            }
            var executableName = OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server";
            var servers = Directory.GetFiles(Path.Combine(stage, "0"), executableName, SearchOption.AllDirectories);
            if (servers.Length != 1) throw new InvalidDataException("运行包未包含唯一的 llama-server。");
            var executable = servers[0]; var executableDirectory = Path.GetDirectoryName(executable)!;
            for (var index = 1; index < bundle.Assets.Length; index++)
            {
                foreach (var library in Directory.GetFiles(Path.Combine(stage, index.ToString()), "*", SearchOption.AllDirectories)
                    .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(p).Contains(".so", StringComparison.Ordinal)))
                {
                    var destination = Path.Combine(executableDirectory, Path.GetFileName(library));
                    if (File.Exists(destination))
                    {
                        await using var a = File.OpenRead(library); await using var b = File.OpenRead(destination);
                        if (!(await SHA256.HashDataAsync(a, token)).AsEnumerable().SequenceEqual(await SHA256.HashDataAsync(b, token)))
                            throw new InvalidDataException("Runtime dependency archives disagree; not overwriting a DLL.");
                    }
                    else File.Copy(library, destination);
                }
            }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var version = await RuntimeProcessProbe.RunAsync(executable, ["--version"], TimeSpan.FromSeconds(20), token, true);
            if (version.ExitCode != 0) throw new ProviderException("runtime_dependency_missing", "运行包无法启动，请检查显卡驱动及系统依赖；已有后端未被替换。");
            if (bundle.Backend != "cpu" && !(await GpuInventory.ListDevicesAsync(executable, bundle.Backend, token)).Any(d => d.IsHardwareGpu))
                throw new ProviderException("runtime_no_gpu", "新运行后端未检测到可用显卡，未设为默认。请使用 Vulkan／CPU 或检查驱动。");
            token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(stage, executable);
            if (Directory.Exists(target))
            {
                // Never reuse an altered target merely because its directory has a trusted-looking name.
                var stagedFiles = Directory.GetFiles(stage, "*", SearchOption.AllDirectories);
                if (stagedFiles.Length != Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length)
                    throw new InvalidDataException("Installed runtime has unexpected files.");
                foreach (var file in stagedFiles)
                {
                    var installed = Path.Combine(target, Path.GetRelativePath(stage, file));
                    if (!File.Exists(installed) || new FileInfo(installed).Length != new FileInfo(file).Length)
                        throw new InvalidDataException("Existing runtime differs from the verified package; not overwriting loaded files.");
                    await using var a = File.OpenRead(file); await using var b = File.OpenRead(installed);
                    if (!(await SHA256.HashDataAsync(a, token)).AsEnumerable().SequenceEqual(await SHA256.HashDataAsync(b, token)))
                        throw new InvalidDataException("Existing runtime checksum differs; not replacing loaded files.");
                }
            }
            else Directory.Move(stage, target);
            var entry = new RuntimeInstallation(bundle.Backend, directoryName, relative, bundle.Assets.Select(a => a.Sha256).ToArray());
            var metadata = Path.Combine(baseDirectory, "current.json");
            var temporary = metadata + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entry, RuntimeJsonContext.Default.RuntimeInstallation), token);
                if (File.Exists(metadata)) File.Copy(metadata, Path.Combine(baseDirectory, "previous.json"), true);
                File.Move(temporary, metadata, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return new InstalledRuntime(bundle.Backend, bundle.Tag, Path.Combine(target, relative));
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifetime.CancelAsync();
        await _installGate.WaitAsync();
        try { _http.Dispose(); }
        finally { _installGate.Release(); }
    }
}

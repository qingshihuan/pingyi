using System.Text.Json;
using PingYi.Core;

namespace PingYi.Infrastructure;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly SemaphoreSlim[] FileGates = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly string _settingsFile;
    private readonly SemaphoreSlim _gate;
    public JsonSettingsStore(AppDataPaths paths) : this(paths.SettingsFile) { }
    public JsonSettingsStore(string settingsFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFile);
        _settingsFile = Path.GetFullPath(settingsFile);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _gate = FileGates[(int)((uint)comparer.GetHashCode(_settingsFile) % (uint)FileGates.Length)];
    }
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(_settingsFile, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Null) return new AppSettings();
            var settings = root.Deserialize(PingYiJsonContext.Default.AppSettings) ?? new AppSettings();
            // Source-generated JSON can supply CLR defaults for absent optional members.
            // An explicit false is a user choice; an absent automatic-task flag is not.
            settings = settings with
            {
                AutomaticCaptureEnabled = !root.TryGetProperty("automaticCaptureEnabled", out _) || settings.AutomaticCaptureEnabled
            };
            // Existing JSON without a schema predates today's defaults. A missing file is
            // different: it is a new install and should offer Basic setup, not migrate it.
            var schema = root.TryGetProperty("schemaVersion", out var version) && version.TryGetInt32(out var value) ? value : 0;
            if (schema < 11)
            {
                settings = settings with
                {
                    SchemaVersion = schema,
                    OcrProviderId = root.TryGetProperty("ocrProviderId", out _) ? settings.OcrProviderId : "local-paddle",
                    TranslationProviderId = root.TryGetProperty("translationProviderId", out _) ? settings.TranslationProviderId : "local-argos"
                };
            }
            return settings.Normalize();
        }
        catch (Exception error) when (error is JsonException or FileNotFoundException or DirectoryNotFoundException)
        {
            return new AppSettings(); // Permission and other I/O failures remain visible.
        }
        finally { _gate.Release(); }
    }
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
            temporaryPath = _settingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, settings.Normalize(), PingYiJsonContext.Default.AppSettings, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _settingsFile, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            _gate.Release();
        }
    }
}

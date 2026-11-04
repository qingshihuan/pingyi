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
            var schema = root.TryGetProperty("schemaVersion", out var version) && version.TryGetInt32(out var value) ? value : 0;
            if (schema > AppSettings.CurrentSchemaVersion)
                throw new ProviderException("settings_future_schema", "配置来自更新版本，请勿用旧程序覆盖。 / Configuration belongs to a newer version; refusing to overwrite it.");
            var settings = root.Deserialize(PingYiJsonContext.Default.AppSettings) ?? new AppSettings();
            settings = settings with
            {
                SchemaVersion = schema,
                AutomaticCaptureEnabled = !root.TryGetProperty("automaticCaptureEnabled", out _) || settings.AutomaticCaptureEnabled
            };
            if (schema < 11)
            {
                settings = settings with
                {
                    OcrProviderId = root.TryGetProperty("ocrProviderId", out _) ? settings.OcrProviderId : "local-paddle",
                    TranslationProviderId = root.TryGetProperty("translationProviderId", out _) ? settings.TranslationProviderId : "local-argos"
                };
            }
            return ConfigurationMigration.Upgrade(settings.Normalize());
        }
        catch (Exception error) when (error is JsonException or FileNotFoundException or DirectoryNotFoundException)
        {
            return new AppSettings();
        }
        finally { _gate.Release(); }
    }
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = ConfigurationMigration.Upgrade(settings.Normalize());
        normalized.ModelConfiguration!.Validate();
        await _gate.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
            if (File.Exists(_settingsFile))
            {
                var previousSchema = 0;
                try
                {
                    using var old = JsonDocument.Parse(await File.ReadAllTextAsync(_settingsFile, cancellationToken));
                    if (old.RootElement.ValueKind == JsonValueKind.Object && old.RootElement.TryGetProperty("schemaVersion", out var v))
                        v.TryGetInt32(out previousSchema);
                }
                catch (JsonException) { }
                if (previousSchema > AppSettings.CurrentSchemaVersion)
                    throw new ProviderException("settings_future_schema", "拒绝覆盖更新版本配置。 / Refusing to overwrite a newer configuration.");
                // First migration keeps the original bytes. Never replace an earlier backup.
                if (previousSchema < 15 && !File.Exists(_settingsFile + ".pre-v15.bak"))
                    File.Copy(_settingsFile, _settingsFile + ".pre-v15.bak", overwrite: false);
            }
            temporaryPath = _settingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, PingYiJsonContext.Default.AppSettings, cancellationToken);
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

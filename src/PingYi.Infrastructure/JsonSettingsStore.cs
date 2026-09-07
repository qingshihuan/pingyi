using System.Text.Json;
using PingYi.Core;

namespace PingYi.Infrastructure;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _settingsFile;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSettingsStore(AppDataPaths paths) : this(paths.SettingsFile)
    {
    }

    public JsonSettingsStore(string settingsFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFile);
        _settingsFile = Path.GetFullPath(settingsFile);
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Allow another store/process to replace the file while this reader
            // finishes reading its complete snapshot (including on Windows).
            await using var stream = new FileStream(
                _settingsFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = await JsonSerializer.DeserializeAsync(
                stream,
                PingYiJsonContext.Default.AppSettings,
                cancellationToken);
            return (settings ?? new AppSettings()).Normalize();
        }
        catch (Exception exception) when (
            exception is JsonException or FileNotFoundException or DirectoryNotFoundException)
        {
            // A missing or malformed file is recoverable; permission and other
            // I/O failures must still be visible instead of silently resetting.
            return new AppSettings();
        }
        finally
        {
            _gate.Release();
        }
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
            // Keep the replacement on the same filesystem and avoid collisions
            // even when independent store instances save concurrently.
            temporaryPath = _settingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings.Normalize(),
                    PingYiJsonContext.Default.AppSettings,
                    cancellationToken);
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
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Do not mask the original save/cancellation failure.
                }
            }

            _gate.Release();
        }
    }
}

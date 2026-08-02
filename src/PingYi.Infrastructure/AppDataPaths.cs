namespace PingYi.Infrastructure;

public sealed class AppDataPaths
{
    public AppDataPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            ConfigDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppEdition.DataDirectoryName);
            DataDirectory = ConfigDirectory;
        }
        else
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            ConfigDirectory = Path.Combine(
                string.IsNullOrWhiteSpace(xdgConfig) ? Path.Combine(userProfile, ".config") : xdgConfig,
                AppEdition.LinuxDataDirectoryName);
            DataDirectory = Path.Combine(
                string.IsNullOrWhiteSpace(xdgData) ? Path.Combine(userProfile, ".local", "share") : xdgData,
                AppEdition.LinuxDataDirectoryName);
        }

        var modelOverride = Environment.GetEnvironmentVariable("PINGYI_MODEL_DIR");
        ModelDirectory = string.IsNullOrWhiteSpace(modelOverride)
            ? Path.Combine(DataDirectory, "models")
            : Path.GetFullPath(modelOverride);
        var bundledOverride = Environment.GetEnvironmentVariable("PINGYI_BUNDLED_MODEL_DIR");
        BundledModelDirectory = string.IsNullOrWhiteSpace(bundledOverride)
            ? Path.Combine(AppContext.BaseDirectory, "offline-models")
            : Path.GetFullPath(bundledOverride);
        ModelSearchDirectories = Directory.Exists(BundledModelDirectory)
            ? [ModelDirectory, BundledModelDirectory]
            : [ModelDirectory];
        ManagedModelDirectory = Path.Combine(ModelDirectory, "managed-vlm");
        var llamaRuntimeOverride = Environment.GetEnvironmentVariable("PINGYI_LLAMA_RUNTIME_DIR");
        LlamaRuntimeDirectory = string.IsNullOrWhiteSpace(llamaRuntimeOverride)
            ? Path.Combine(AppContext.BaseDirectory, "llama-runtime")
            : Path.GetFullPath(llamaRuntimeOverride);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ModelDirectory);
        Directory.CreateDirectory(ManagedModelDirectory);
    }

    public string ConfigDirectory { get; }
    public string DataDirectory { get; }
    public string ModelDirectory { get; }
    public string BundledModelDirectory { get; }
    public string ManagedModelDirectory { get; }
    public string LlamaRuntimeDirectory { get; }
    public IReadOnlyList<string> ModelSearchDirectories { get; }
    public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");
    public string SecretsDirectory => Path.Combine(ConfigDirectory, "secrets");
}

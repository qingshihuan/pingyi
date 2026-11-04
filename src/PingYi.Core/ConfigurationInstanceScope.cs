using System.Security.Cryptography;
using System.Text;

namespace PingYi.Core;

/// <summary>Explicit isolated configurations must not attach to the normal desktop/bridge instance.</summary>
public static class ConfigurationInstanceScope
{
    public static string Current => ForDirectory(Environment.GetEnvironmentVariable("PINGYI_CONFIG_DIR"));
    public static string ForDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return "";
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        return "-config-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..24];
    }
}

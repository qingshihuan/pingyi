namespace PingYi.Infrastructure;

public static class AppEdition
{
    public static bool IsComplete { get; } =
        string.Equals(
            Environment.GetEnvironmentVariable("PINGYI_EDITION"),
            "complete",
            StringComparison.OrdinalIgnoreCase) ||
        File.Exists(Path.Combine(AppContext.BaseDirectory, "pingyi-complete.edition"));

    public static string ProductName => IsComplete ? "截屏释义 完全版" : "截屏释义";
    public static string DataDirectoryName => IsComplete ? "PingYiComplete" : "PingYi";
    public static string LinuxDataDirectoryName => IsComplete ? "pingyi-complete" : "pingyi";
}

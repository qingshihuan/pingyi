namespace PingYi.Infrastructure;

public static class AppEdition
{
    public static bool IsComplete { get; } =
        string.Equals(
            Environment.GetEnvironmentVariable("PINGYI_EDITION"),
            "complete",
            StringComparison.OrdinalIgnoreCase) ||
        File.Exists(Path.Combine(AppContext.BaseDirectory, "pingyi-complete.edition"));

    public static string ProductName => IsComplete ? "屏译 完全版" : "屏译";
    public static string DataDirectoryName => IsComplete ? "PingYiComplete" : "PingYi";
    public static string LinuxDataDirectoryName => IsComplete ? "pingyi-complete" : "pingyi";
}

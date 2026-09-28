namespace PingYi.Infrastructure;

public static class AppEdition
{
    // Source builds and release packages use the same, sole supported edition.
    public static bool IsComplete => true;

    public static string ProductName => "截屏释义 完全版";
    public static string DataDirectoryName => "PingYiComplete";
    public static string LinuxDataDirectoryName => "pingyi-complete";
}

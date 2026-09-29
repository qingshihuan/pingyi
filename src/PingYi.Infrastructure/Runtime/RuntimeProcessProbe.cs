using System.Diagnostics;
using System.Text;
using PingYi.Core;

namespace PingYi.Infrastructure;

internal sealed record RuntimeCommandResult(int ExitCode, string Output, string Error);

internal static class RuntimeProcessProbe
{
    public static async Task<RuntimeCommandResult> RunAsync(string executable, IEnumerable<string> arguments,
        TimeSpan timeout, CancellationToken token, bool backendEnvironment = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.IsPathFullyQualified(executable)) start.WorkingDirectory = Path.GetDirectoryName(executable)!;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (backendEnvironment) ConfigureEnvironment(start);
        await using var owner = OwnedProcessScope.Start(start);
        var process = owner.Process;
        var output = ReadBoundedAsync(process.StandardOutput, deadline.Token);
        var error = ReadBoundedAsync(process.StandardError, deadline.Token);
        await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, error).ConfigureAwait(false);
        return new RuntimeCommandResult(process.ExitCode, await output, await error);
    }

    internal static void ConfigureEnvironment(ProcessStartInfo start)
    {
        // These values apply only to our child. The system and external model servers are untouched.
        foreach (var key in new[] { "CUDA_VISIBLE_DEVICES", "HIP_VISIBLE_DEVICES", "ROCR_VISIBLE_DEVICES", "GGML_VK_VISIBLE_DEVICES" })
            start.Environment.Remove(key);
        start.Environment["CUDA_DEVICE_ORDER"] = "PCI_BUS_ID";
        if (!OperatingSystem.IsWindows() && Path.IsPathFullyQualified(start.FileName))
        {
            var root = Path.GetDirectoryName(start.FileName)!;
            start.Environment["LD_LIBRARY_PATH"] = root + ":" + Path.Combine(root, "lib") + ":" +
                Path.Combine(root, "..", "lib") + ":" + (start.Environment.TryGetValue("LD_LIBRARY_PATH", out var existing) ? existing : "");
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, token)) != 0)
        {
            if (text.Length + count > 262144) throw new InvalidDataException("Hardware probe output exceeded its limit.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}

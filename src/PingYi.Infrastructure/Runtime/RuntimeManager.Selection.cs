using System.Runtime.ExceptionServices;
using PingYi.Core;

namespace PingYi.Infrastructure;

public sealed partial class RuntimeManager
{
    /// <summary>Probe installed candidates in preference order without stopping the active model or downloading.</summary>
    public async Task<InstalledRuntime?> UsableInstalledAsync(string requested, string selection, CancellationToken token)
    {
        requested = ManagedRuntimeBackends.Normalize(requested);
        var hardware = requested == "auto" ? await DetectAsync(false, token) : [];
        var candidates = RuntimeHardwarePolicy.Candidates(requested, selection, hardware, OperatingSystem.IsWindows())
            .Select(Find).OfType<InstalledRuntime>();
        return await ValidateCandidatesAsync(candidates, selection, ValidateInstalledAsync, token);
    }

    internal static async Task<InstalledRuntime?> ValidateCandidatesAsync(IEnumerable<InstalledRuntime> candidates,
        string selection, Func<InstalledRuntime, string, CancellationToken, Task> validate, CancellationToken token)
    {
        Exception? lastError = null;
        foreach (var runtime in candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await validate(runtime, selection, token);
                token.ThrowIfCancellationRequested();
                return runtime;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is ProviderException or IOException or InvalidOperationException or
                System.ComponentModel.Win32Exception or TimeoutException or OperationCanceledException)
            {
                lastError = error;
            }
        }
        token.ThrowIfCancellationRequested();
        if (lastError is not null) ExceptionDispatchInfo.Throw(lastError);
        return null;
    }
}

namespace PingYi.Core;

/// <summary>A GPU is usable only after inference succeeds; CPU must pass the same verification.</summary>
public static class AutomaticRuntimeVerification
{
    public static async Task<bool> VerifyAsync(bool allowCpuFallback, Func<CancellationToken, Task> verify,
        Func<CancellationToken, Task> startCpu, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { await verify(token); }
        catch (Exception error) when (allowCpuFallback && !token.IsCancellationRequested &&
            error is ProviderException or HttpRequestException or IOException or TimeoutException or OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            await startCpu(token);
            token.ThrowIfCancellationRequested();
            await verify(token);
            token.ThrowIfCancellationRequested();
            return true;
        }
        token.ThrowIfCancellationRequested();
        return false;
    }
}

using PingYi.Core;

namespace PingYi.Core.Tests;

public sealed class AutomaticRuntimeVerificationTests
{
    [Fact]
    public async Task Successful_gpu_inference_does_not_start_cpu()
    {
        Assert.False(await AutomaticRuntimeVerification.VerifyAsync(true, _ => Task.CompletedTask,
            _ => throw new InvalidOperationException("Must retain the verified GPU"), default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_timed_out_gpu_inference_retries_the_same_checks_on_cpu(bool timeout)
    {
        var calls = new List<string>();
        var cpu = false;
        var fallback = await AutomaticRuntimeVerification.VerifyAsync(true, _ =>
        {
            calls.Add(cpu ? "verify-cpu" : "verify-gpu");
            if (!cpu)
            {
                if (timeout) throw new OperationCanceledException("Synthetic request timeout, not user cancellation");
                throw new ProviderException("profile_vision_unverified", "Synthetic unreadable image");
            }
            return Task.CompletedTask;
        }, _ => { calls.Add("start-cpu"); cpu = true; return Task.CompletedTask; }, default);
        Assert.True(fallback);
        Assert.Equal(new[] { "verify-gpu", "start-cpu", "verify-cpu" }, calls);
    }

    [Fact]
    public async Task Failed_cpu_verification_does_not_report_success()
    {
        var count = 0;
        await Assert.ThrowsAsync<ProviderException>(() => AutomaticRuntimeVerification.VerifyAsync(true,
            _ => { count++; throw new ProviderException("profile_vision_unverified", "Synthetic verification failure"); },
            _ => Task.CompletedTask, default));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Explicit_backend_failure_does_not_fall_back()
    {
        await Assert.ThrowsAsync<IOException>(() => AutomaticRuntimeVerification.VerifyAsync(false,
            _ => throw new IOException("Synthetic explicit GPU failure"),
            _ => throw new InvalidOperationException("Must not switch a manual backend"), default));
    }

    [Fact]
    public async Task User_cancellation_never_starts_cpu()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AutomaticRuntimeVerification.VerifyAsync(true,
            token => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            _ => throw new InvalidOperationException("Must not start after cancellation"), cancellation.Token));
    }
}

using PingYi.Core;

namespace PingYi.Core.Tests;

public sealed class ShutdownTests
{
    [Fact]
    public async Task A_failing_UI_or_hotkey_cleanup_cannot_skip_backends()
    {
        var stopped = false;
        var failure = await Assert.ThrowsAsync<AggregateException>(() => ShutdownWork.RunAllAsync(
            () => throw new InvalidOperationException("synthetic UI failure"),
            async () => { await Task.Yield(); stopped = true; },
            () => throw new IOException("synthetic hotkey failure")));
        Assert.True(stopped);
        Assert.Equal(2, failure.InnerExceptions.Count);
    }

    [Fact]
    public async Task Backend_cleanup_starts_without_waiting_for_a_busy_bridge()
    {
        var bridge = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = ShutdownWork.RunAllAsync(() => bridge.Task, () => { backend.SetResult(); return Task.CompletedTask; });
        await backend.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(cleanup.IsCompleted);
        bridge.SetResult();
        await cleanup;
    }

    [Fact]
    public async Task Repeated_and_reentrant_exit_requests_share_one_task()
    {
        var once = new ShutdownOnce();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task? reentrant = null;
        var first = once.RunAsync(() =>
        {
            calls++;
            reentrant = once.RunAsync(() => throw new Exception("must not run"));
            return release.Task;
        });
        var second = once.RunAsync(() => throw new Exception("must not run"));
        Assert.Same(first, reentrant);
        Assert.Same(first, second);
        Assert.Equal(1, calls);
        release.SetResult();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Concurrent_disposal_observes_the_same_failure()
    {
        var once = new ShutdownOnce();
        var first = once.RunAsync(() => throw new IOException("synthetic stop failure"));
        var second = once.RunAsync(() => Task.CompletedTask);
        Assert.Same(first, second);
        await Assert.ThrowsAsync<IOException>(() => first);
        await Assert.ThrowsAsync<IOException>(() => second);
    }
}

using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class RuntimeSelectionTests
{
    private static InstalledRuntime Runtime(string backend) => new(backend, "synthetic", backend);

    [Fact]
    public async Task Usable_gpu_is_selected_without_probing_cpu()
    {
        var calls = new List<string>();
        var result = await RuntimeManager.ValidateCandidatesAsync([Runtime("vulkan"), Runtime("cpu")], "auto",
            (runtime, _, _) => { calls.Add(runtime.Backend); return Task.CompletedTask; }, default);
        Assert.Equal("vulkan", result!.Backend);
        Assert.Equal(new[] { "vulkan" }, calls);
    }

    [Theory]
    [InlineData("no-gpu")]
    [InlineData("dependency")]
    [InlineData("timeout")]
    public async Task Unusable_gpu_backends_fall_back_to_cpu(string failure)
    {
        var calls = new List<string>();
        var result = await RuntimeManager.ValidateCandidatesAsync([Runtime("rocm"), Runtime("vulkan"), Runtime("cpu")], "auto",
            (runtime, _, _) =>
            {
                calls.Add(runtime.Backend);
                if (runtime.Backend == "cpu") return Task.CompletedTask;
                throw failure switch
                {
                    "no-gpu" => new ProviderException("runtime_no_gpu", "Synthetic missing GPU"),
                    "dependency" => new System.ComponentModel.Win32Exception("Synthetic missing driver dependency"),
                    _ => new TimeoutException("Synthetic GPU probe timeout")
                };
            }, default);
        Assert.Equal("cpu", result!.Backend);
        Assert.Equal(new[] { "rocm", "vulkan", "cpu" }, calls);
    }

    [Fact]
    public async Task Manual_gpu_failure_is_reported_without_selecting_cpu()
    {
        var candidates = RuntimeHardwarePolicy.Candidates("vulkan", "auto", [], true).Select(Runtime);
        var failure = new ProviderException("runtime_no_gpu", "Synthetic missing GPU");
        var actual = await Assert.ThrowsAsync<ProviderException>(() => RuntimeManager.ValidateCandidatesAsync(candidates,
            "auto", (_, _, _) => throw failure, default));
        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task Cancellation_during_gpu_probe_does_not_start_cpu()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = new List<string>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RuntimeManager.ValidateCandidatesAsync(
            [Runtime("vulkan"), Runtime("cpu")], "auto", (runtime, _, token) =>
            {
                calls.Add(runtime.Backend);
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }, cancellation.Token));
        Assert.Equal(new[] { "vulkan" }, calls);
    }

    [Fact]
    public async Task Missing_backends_are_distinct_from_an_unusable_cpu()
    {
        Assert.Null(await RuntimeManager.ValidateCandidatesAsync([], "auto",
            (_, _, _) => throw new InvalidOperationException("No runtime to probe"), default));
        await Assert.ThrowsAsync<IOException>(() => RuntimeManager.ValidateCandidatesAsync([Runtime("cpu")], "auto",
            (_, _, _) => throw new IOException("Synthetic CPU executable failure"), default));
    }
}

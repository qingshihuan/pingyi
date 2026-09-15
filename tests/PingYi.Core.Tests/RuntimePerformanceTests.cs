using System.Diagnostics;
using System.Reflection;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public class RuntimePerformanceTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 4)]
    [InlineData(64, 4)]
    public void Ocr_leaves_cpu_capacity_for_the_desktop(int processors, int expected) =>
        Assert.Equal(expected, RuntimePolicy.OcrThreadCount(processors));

    [Fact]
    public void Native_options_use_bounded_sequential_threads()
    {
        using var options = OcrMemory.CreateSessionOptions();
        Assert.InRange(options.IntraOpNumThreads, 1, 4);
        Assert.Equal(1, options.InterOpNumThreads);
        Assert.Equal(ExecutionMode.ORT_SEQUENTIAL, options.ExecutionMode);
    }

    [Fact]
    public void Dense_output_borrows_memory_instead_of_copying_logits()
    {
        var data = new float[4 * 1024 * 1024];
        var tensor = new DenseTensor<float>(data, new[] { 1, data.Length });
        _ = OcrMemory.ReadValues(tensor).Length; // Warm the helper before measuring.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var values = OcrMemory.ReadValues(tensor);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        data[123] = 0.875f;
        Assert.Equal(0.875f, values[123]);
        Assert.Equal(data.Length, values.Length);
        Assert.True(allocated < 4096, $"Borrowing the dense tensor allocated {allocated} bytes.");
    }

    [Fact]
    public void Reversed_output_preserves_the_previous_enumeration_order()
    {
        var tensor = new DenseTensor<float>(new float[] { 1, 2, 3, 4, 5, 6 }, new[] { 2, 3 }, reverseStride: true);
        Assert.Equal(tensor.ToArray(), OcrMemory.ReadValues(tensor).ToArray());
    }

    [Fact]
    public void Remembered_managed_model_does_not_preload_in_local_only_mode()
    {
        var settings = new AppSettings
        {
            ManagedRuntimeEnabled = true,
            ManagedModelPackageId = ManagedMultimodalModels.Recommended.Id,
            CustomTranslationEndpoint = AppSettings.ManagedModelEndpoint
        };
        Assert.False(RuntimePolicy.UsesManagedRuntime(settings));
        Assert.True(RuntimePolicy.UsesManagedRuntime(settings with { TranslationProviderId = "custom-chat" }));
        Assert.True(RuntimePolicy.UsesManagedRuntime(settings with { OcrProviderId = "local-vlm-corrected" }));
        Assert.False(RuntimePolicy.UsesManagedRuntime(settings with
        {
            TranslationProviderId = "custom-chat", CustomTranslationEndpoint = "https://example.com/v1/chat/completions"
        }));
    }

    [Fact]
    public void Passive_refresh_expires_and_invalidates_when_settings_change()
    {
        var clock = new TestClock();
        var policy = new PassiveRefreshPolicy(TimeSpan.FromSeconds(30), clock);
        var settings = new AppSettings();
        Assert.True(policy.ShouldRefresh(settings));
        policy.RecordRefresh(settings);
        for (var i = 0; i < 100; i++) Assert.False(policy.ShouldRefresh(settings));
        Assert.True(policy.ShouldRefresh(settings with { TargetLanguage = "en" }));
        clock.Ticks += TimeSpan.FromSeconds(30).Ticks;
        Assert.True(policy.ShouldRefresh(settings));
    }

    [Fact]
    public async Task Idle_engine_is_released_and_restarts_on_demand()
    {
        await using var engine = new EngineProcessClient(new AppDataPaths(), idleTimeout: TimeSpan.FromMilliseconds(200));
        Assert.Null(ProcessOf(engine));
        await engine.CallAsync("health");
        var first = ProcessOf(engine);
        Assert.NotNull(first);
        await WaitUntilAsync(() => ProcessOf(engine) is null);
        var health = await engine.CallAsync("health");
        Assert.True(health.TryGetProperty("translationModelsReady", out _));
        Assert.NotSame(first, ProcessOf(engine));
    }

    [Fact]
    public async Task Idle_timer_cannot_kill_a_gate_owned_request()
    {
        await using var engine = new EngineProcessClient(new AppDataPaths(), idleTimeout: TimeSpan.FromMilliseconds(200));
        await engine.CallAsync("health");
        var gate = Field<SemaphoreSlim>(engine, "_gate");
        await gate.WaitAsync();
        var first = ProcessOf(engine);
        try
        {
            await Task.Delay(500);
            Assert.Same(first, ProcessOf(engine));
        }
        finally { gate.Release(); }
        await engine.CallAsync("health"); // Rearms idle timer as a real request would.
        await WaitUntilAsync(() => ProcessOf(engine) is null);
    }

    [Fact]
    public async Task Model_mutation_recycles_directory_snapshot_without_changing_files()
    {
        await using var engine = new EngineProcessClient(new AppDataPaths());
        await engine.CallAsync("health");
        // Unsupported scope is intentionally a no-op: never delete real user models in tests.
        var result = await engine.CallAsync("delete_models", new System.Text.Json.Nodes.JsonObject { ["scope"] = "test-no-op" });
        Assert.Empty(result.GetProperty("deleted").EnumerateArray());
        Assert.Null(ProcessOf(engine));
        await engine.CallAsync("health");
        Assert.NotNull(ProcessOf(engine));
    }

    [Fact]
    public async Task Concurrent_dispose_callers_wait_for_the_same_cleanup()
    {
        var engine = new EngineProcessClient(new AppDataPaths());
        var gate = Field<SemaphoreSlim>(engine, "_gate");
        await gate.WaitAsync();
        var first = engine.DisposeAsync().AsTask();
        var second = engine.DisposeAsync().AsTask();
        try { Assert.False(first.IsCompleted); Assert.False(second.IsCompleted); }
        finally { gate.Release(); }
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.CallAsync("health"));
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    private static Process? ProcessOf(EngineProcessClient engine) => Field<Process?>(engine, "_process");
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(25);
        Assert.True(condition());
    }
    private sealed class TestClock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }
}

using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class RuntimeSwitchTests
{
    [Fact]
    public void New_install_defaults_to_Vulkan_but_explicit_Auto_remains_optional()
    {
        Assert.Equal("vulkan", new AppSettings().ManagedRuntimeBackend);
        Assert.Equal(ManagedRuntimeBackends.Vulkan, ManagedRuntimeBackends.All[0]);
        Assert.Equal("vulkan", ManagedRuntimeBackends.Normalize(null));
        Assert.Equal("vulkan", ManagedRuntimeBackends.Normalize("unknown"));
        Assert.Equal("auto", new AppSettings { ManagedRuntimeBackend = "auto" }.Normalize().ManagedRuntimeBackend);
    }

    [Theory]
    [InlineData("auto", "vulkan")]
    [InlineData("cpu", "cpu")]
    [InlineData("rocm", "rocm")]
    [InlineData("cuda12", "cuda12")]
    [InlineData("cuda13", "cuda13")]
    [InlineData("vulkan", "vulkan")]
    [InlineData(null, "vulkan")]
    public void Upgrade_migrates_only_the_former_automatic_default(string? backend, string expected)
    {
        var settings = new AppSettings { SchemaVersion = 12, ManagedRuntimeBackend = backend!, UiLanguage = "en-US", RuntimeAllowMirrors = true };
        var result = settings.Normalize();
        Assert.Equal(expected, result.ManagedRuntimeBackend);
        Assert.Equal("en-US", result.UiLanguage);
        Assert.True(result.RuntimeAllowMirrors);
        Assert.Equal(result, result.Normalize());
    }

    [Fact]
    public async Task Explicit_device_and_preferences_survive_save_and_reload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pingyi-switch-" + Guid.NewGuid().ToString("N"));
        try
        {
            RuntimeDevice[] devices = [new("rocm", "ROCm0", "Synthetic AMD", 20480, 19000)];
            var choice = RuntimeDeviceChoice.Encode(devices[0], devices);
            var source = new AppSettings { SchemaVersion = 12, ManagedRuntimeBackend = "auto", ManagedRuntimeDevice = choice };
            Assert.Equal("auto", source.Normalize().ManagedRuntimeBackend);
            var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
            await store.SaveAsync(source);
            Assert.Equal(choice, (await store.LoadAsync()).ManagedRuntimeDevice);
            await store.SaveAsync(source with { SchemaVersion = 13, ManagedRuntimeBackend = "rocm" });
            Assert.Equal("rocm", (await store.LoadAsync()).ManagedRuntimeBackend);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Switch_prepares_then_activates_then_saves_without_uninstalling_old_files()
    {
        var calls = new List<string>();
        await RuntimeChangeTransaction.ApplyAsync(_ => Step("probe"), _ => Step("activate"),
            _ => Step("save"), () => Step("restore"), default);
        Assert.Equal(new[] { "probe", "activate", "save" }, calls);
        Task Step(string value) { calls.Add(value); return Task.CompletedTask; }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Failure_never_commits_and_recovers_only_after_activation(int failingStage)
    {
        var calls = new List<string>();
        await Assert.ThrowsAsync<IOException>(() => RuntimeChangeTransaction.ApplyAsync(
            _ => Step(0), _ => Step(1), _ => Step(2), () => Step(3), default));
        Assert.Equal(failingStage == 0 ? new[] { "0" } : failingStage == 1 ? new[] { "0", "1", "3" } : new[] { "0", "1", "2", "3" }, calls);
        Task Step(int stage) { calls.Add(stage.ToString()); if (stage == failingStage) throw new IOException("Synthetic failure"); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Cancellation_after_activation_restores_with_an_independent_token()
    {
        using var cancelled = new CancellationTokenSource();
        var restored = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RuntimeChangeTransaction.ApplyAsync(
            _ => Task.CompletedTask, _ => { cancelled.Cancel(); return Task.CompletedTask; },
            _ => throw new Xunit.Sdk.XunitException("Must not save"),
            () => { restored = true; return Task.CompletedTask; }, cancelled.Token));
        Assert.True(restored);
    }

    [Fact]
    public async Task Recovery_failure_is_not_suppressed()
    {
        var failure = await Assert.ThrowsAsync<AggregateException>(() => RuntimeChangeTransaction.ApplyAsync(
            _ => Task.CompletedTask, _ => throw new IOException("activation"), _ => Task.CompletedTask,
            () => throw new IOException("recovery"), default));
        Assert.Equal(2, failure.InnerExceptions.Count);
    }
}

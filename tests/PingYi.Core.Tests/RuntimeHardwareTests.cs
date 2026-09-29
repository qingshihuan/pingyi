using PingYi.Core;
using PingYi.Infrastructure;

namespace PingYi.Core.Tests;

public sealed class RuntimeHardwareTests
{
    [Theory]
    [InlineData(6.1, "582.10", true, "cuda12")]
    [InlineData(8.6, "582.10", true, "cuda13")]
    [InlineData(8.6, "551.80", true, "cuda12")]
    [InlineData(12.0, "551.80", true, "vulkan")]
    [InlineData(6.1, "551.80", false, "vulkan")]
    [InlineData(6.1, "570.80", false, "cuda12")]
    [InlineData(8.6, "unknown", true, "vulkan")]
    public void Recommendation_uses_architecture_driver_and_platform(double cc, string driver, bool windows, string expected)
    {
        var gpu = new GpuHardware("GPU-a", "Synthetic NVIDIA", GpuVendor.Nvidia, driver, 8192, cc);
        Assert.Equal(expected, RuntimeHardwarePolicy.Recommend([gpu], windows)[0]);
    }
    [Fact]
    public void Amd_is_a_probe_candidate_not_a_guarantee_and_unknown_keeps_fallbacks()
    {
        Assert.Equal(new[] { "rocm", "vulkan", "cpu" }, RuntimeHardwarePolicy.Recommend([new("pci-1", "AMD", GpuVendor.Amd)], true));
        Assert.Equal(new[] { "vulkan", "cpu" }, RuntimeHardwarePolicy.Recommend([], true));
        Assert.Equal(new[] { "vulkan" }, RuntimeHardwarePolicy.Candidates("vulkan", "auto", [], true));
        Assert.Equal(new[] { "cpu" }, RuntimeHardwarePolicy.Candidates("cpu", "auto", [], true));
    }
    [Fact]
    public void Explicit_device_is_bound_to_inventory_and_cannot_fallback_to_another_backend()
    {
        RuntimeDevice[] devices = [new("cuda13", "CUDA0", "GPU A", 8192, 7000), new("cuda13", "CUDA1", "GPU B", 16384, 12000)];
        var selection = RuntimeDeviceChoice.Encode(devices[1], devices);
        Assert.Equal(devices[1], RuntimeDeviceChoice.Resolve(selection, "cuda13", devices));
        Assert.Equal(new[] { "cuda13" }, RuntimeHardwarePolicy.Candidates("auto", selection, [], true));
        Assert.Throws<ProviderException>(() => RuntimeHardwarePolicy.Candidates("vulkan", selection, [], true));
        Assert.Throws<ProviderException>(() => RuntimeDeviceChoice.Resolve(selection, "cuda13", devices.Reverse().ToArray()));
        var changedMemory = devices.Select(d => d with { FreeMiB = 1 }).ToArray();
        Assert.Equal("CUDA1", RuntimeDeviceChoice.Resolve(selection, "cuda13", changedMemory)!.Id);
    }
    [Fact]
    public void Automatic_device_skips_software_renderers_and_selects_available_memory()
    {
        RuntimeDevice[] devices = [new("vulkan", "Vulkan0", "llvmpipe", 32768, 30000), new("vulkan", "Vulkan1", "GPU", 8192, 6000)];
        Assert.Equal("Vulkan1", RuntimeDeviceChoice.Resolve("auto", "vulkan", devices)!.Id);
    }
    [Theory]
    [InlineData("cuda13|CUDA0|not-a-fingerprint")]
    [InlineData("cuda13|CUDA0 --host 0.0.0.0|ABCDEF012345678901234567")]
    [InlineData("unknown|GPU0|ABCDEF012345678901234567")]
    public void Settings_reject_invalid_device_arguments(string value) => Assert.Equal("auto", RuntimeDeviceChoice.Normalize(value));
    [Fact]
    public void Nvidia_parser_keeps_distinct_uuids_and_real_64bit_memory()
    {
        var result = GpuInventory.ParseNvidiaCsv("GPU-a, \"NVIDIA A, synthetic\", 0000:01:00.0, 580.12, 24576, 8.6\nGPU-b, NVIDIA B, 0000:02:00.0, 580.12, 8192, 7.5\ninvalid");
        Assert.Equal(2, result.Count);
        Assert.Equal("NVIDIA A, synthetic", result[0].Name);
        Assert.Equal(24576, result[0].MemoryMiB);
        Assert.Equal(8.6, result[0].ComputeCapability);
        Assert.NotEqual(result[0].Id, result[1].Id);
    }
    [Fact]
    public void Backend_devices_are_not_os_display_indices()
    {
        const string text = "Available devices:\n  CUDA0: NVIDIA test (8192 MiB, 6000 MiB free)\n  ROCm0: AMD test (16384 MiB, 12000 MiB free)\n  Vulkan0: GPU test (4096 MiB, 3000 MiB free)";
        Assert.Single(GpuInventory.ParseDevices(text, "cuda13"));
        Assert.Equal("ROCm0", Assert.Single(GpuInventory.ParseDevices(text, "rocm")).Id);
        Assert.Equal("Vulkan0", Assert.Single(GpuInventory.ParseDevices(text, "vulkan")).Id);
    }
    [Fact]
    public void Windows_inventory_does_not_claim_wrapped_adapter_ram_as_actual_memory()
    {
        var item = Assert.Single(GpuInventory.ParseWindowsJson("{\"Name\":\"AMD Test\",\"PNPDeviceID\":\"PCI\\\\VEN_1002\",\"DriverVersion\":\"1.0\"}"));
        Assert.Equal(GpuVendor.Amd, item.Vendor);
        Assert.Null(item.MemoryMiB);
    }
    [Fact]
    public void Configuration_preserves_new_preferences_and_all_six_backends()
    {
        RuntimeDevice[] devices = [new("rocm", "ROCm0", "AMD", 8192, 6000)];
        var selection = RuntimeDeviceChoice.Encode(devices[0], devices);
        var settings = new AppSettings { ManagedRuntimeBackend = "rocm", ManagedRuntimeDevice = selection, RuntimeAllowMirrors = true, RuntimeMirrorPrefixes = "https://relay.example/", UiLanguage = "en-US" }.Normalize();
        Assert.Equal(selection, settings.ManagedRuntimeDevice);
        Assert.True(settings.RuntimeAllowMirrors);
        Assert.Equal("rocm", settings.ManagedRuntimeBackend);
        Assert.Equal(settings, settings.Normalize());
        Assert.Equal(6, ManagedRuntimeBackends.All.Count);
    }
}

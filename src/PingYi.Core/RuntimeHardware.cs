using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PingYi.Core;

public enum GpuVendor { Unknown, Nvidia, Amd, Intel }

public sealed record GpuHardware(string Id, string Name, GpuVendor Vendor,
    string Driver = "", long? MemoryMiB = null, double? ComputeCapability = null, string PciAddress = "")
{
    public override string ToString() => $"{Name}{(MemoryMiB is > 0 ? $" · {MemoryMiB / 1024d:0.#} GiB" : "")}" +
        (string.IsNullOrWhiteSpace(Driver) ? "" : $" · driver {Driver}");
}

public sealed record RuntimeDevice(string Backend, string Id, string Name, long TotalMiB, long FreeMiB)
{
    public bool IsHardwareGpu => TotalMiB > 0 &&
        !Regex.IsMatch(Name, "llvmpipe|lavapipe|software|basic render", RegexOptions.IgnoreCase);
    public bool LikelySharedMemory => Regex.IsMatch(Name, @"AMD Radeon (?:\(TM\) )?Graphics|Intel.*(?:UHD|Iris|HD Graphics)", RegexOptions.IgnoreCase);
    public override string ToString() => $"{Id} · {Name} · {TotalMiB / 1024d:0.#} GiB ({FreeMiB / 1024d:0.#} GiB free)";
}

/// <summary>Device indices belong to a particular backend, not to the OS display order.</summary>
public sealed record RuntimeDeviceChoice(string Value, string Label)
{
    public const string Automatic = "auto";
    public override string ToString() => Label;
    public static string Fingerprint(IEnumerable<RuntimeDevice> devices) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', devices.Select(d =>
            $"{d.Backend}|{d.Id}|{d.Name}|{d.TotalMiB}")))))[..24];
    public static string Encode(RuntimeDevice device, IReadOnlyList<RuntimeDevice> inventory) =>
        $"{device.Backend}|{device.Id}|{Fingerprint(inventory)}";
    public static bool TryParse(string? value, out string backend, out string device, out string fingerprint)
    {
        backend = device = fingerprint = "";
        if (string.IsNullOrWhiteSpace(value) || value == Automatic) return false;
        var parts = value.Split('|');
        if (parts.Length != 3 || parts[0] is not ("cuda12" or "cuda13" or "rocm" or "vulkan") ||
            !Regex.IsMatch(parts[1], "^(CUDA|ROCm|Vulkan|HIP)[0-9]{1,3}$") ||
            !Regex.IsMatch(parts[2], "^[A-F0-9]{24}$")) return false;
        (backend, device, fingerprint) = (parts[0], parts[1], parts[2]);
        return true;
    }
    public static string Normalize(string? value) => value is null or "" or Automatic ? Automatic
        : TryParse(value, out _, out _, out _) ? value : Automatic;

    public static RuntimeDevice? Resolve(string selection, string backend, IReadOnlyList<RuntimeDevice> devices)
    {
        if (selection == Automatic)
            return devices.Where(d => d.IsHardwareGpu).OrderBy(d => d.LikelySharedMemory).ThenByDescending(d => d.FreeMiB).ThenByDescending(d => d.TotalMiB).FirstOrDefault();
        if (!TryParse(selection, out var selectedBackend, out var id, out var fingerprint) || selectedBackend != backend ||
            fingerprint != Fingerprint(devices))
            throw new ProviderException("runtime_device_changed", "显卡列表或运行后端已变化，请重新检测并选择执行显卡；没有改用其他显卡。");
        return devices.FirstOrDefault(d => d.Id == id && d.IsHardwareGpu)
            ?? throw new ProviderException("runtime_device_missing", "所选执行显卡不可用，请重新检测或改选 CPU。");
    }
}

public static class RuntimeHardwarePolicy
{
    // CUDA 13 removed pre-Turing compilation support. CUDA 12.4 cannot target Blackwell.
    // A launch/device probe is still mandatory; toolkit compatibility is not proof of kernel support.
    public static IReadOnlyList<string> Recommend(IReadOnlyList<GpuHardware> hardware, bool windows)
    {
        var backends = new List<string>();
        foreach (var gpu in hardware.OrderByDescending(gpu => gpu.MemoryMiB ?? 0))
        {
            if (gpu.Vendor == GpuVendor.Nvidia && gpu.ComputeCapability is { } cc &&
                int.TryParse(gpu.Driver.Split('.')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var driver))
            {
                if (cc >= 7.5 && driver >= 580) backends.Add("cuda13");
                if (cc >= 5 && (!windows || cc < 10) && driver >= (windows ? 551 : 570)) backends.Add("cuda12");
            }
            else if (gpu.Vendor == GpuVendor.Amd) backends.Add("rocm");
        }
        backends.Add("vulkan"); backends.Add("cpu");
        return backends.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> Candidates(string requested, string device, IReadOnlyList<GpuHardware> hardware, bool windows)
    {
        requested = ManagedRuntimeBackends.Normalize(requested);
        if (requested == "cpu") return ["cpu"];
        if (RuntimeDeviceChoice.TryParse(device, out var selectedBackend, out _, out _))
        {
            if (requested != "auto" && requested != selectedBackend)
                throw new ProviderException("runtime_device_backend", "所选显卡属于其他运行后端，请重新选择执行显卡。");
            return [selectedBackend];
        }
        return requested == "auto" ? Recommend(hardware, windows) : [requested];
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PingYi.Core;

namespace PingYi.Infrastructure;

public sealed class GpuInventory
{
    public async Task<IReadOnlyList<GpuHardware>> DetectAsync(CancellationToken token = default)
    {
        var devices = new List<GpuHardware>();
        // NVIDIA tools report true 64-bit VRAM amounts; Win32_VideoController.AdapterRAM can wrap at 4 GiB.
        try
        {
            var result = await RuntimeProcessProbe.RunAsync(OperatingSystem.IsWindows() ? "nvidia-smi.exe" : "nvidia-smi",
                ["--query-gpu=uuid,name,pci.bus_id,driver_version,memory.total,compute_cap", "--format=csv,noheader,nounits"],
                TimeSpan.FromSeconds(5), token);
            if (result.ExitCode == 0) devices.AddRange(ParseNvidiaCsv(result.Output));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException or IOException) { }
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                var result = await RuntimeProcessProbe.RunAsync(powershell,
                    ["-NoProfile", "-NonInteractive", "-Command",
                     "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new(); @(Get-CimInstance Win32_VideoController | Select-Object Name,PNPDeviceID,DriverVersion) | ConvertTo-Json -Compress"],
                    TimeSpan.FromSeconds(8), token);
                if (result.ExitCode == 0) AddOtherDevices(devices, ParseWindowsJson(result.Output));
            }
            else if (OperatingSystem.IsLinux())
            {
                string pci = "";
                try
                {
                    var result = await RuntimeProcessProbe.RunAsync("lspci", ["-D", "-mm"], TimeSpan.FromSeconds(4), token);
                    if (result.ExitCode == 0) pci = result.Output;
                }
                catch (System.ComponentModel.Win32Exception) { }
                AddOtherDevices(devices, ReadLinuxDevices(pci));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException or IOException or JsonException or UnauthorizedAccessException) { }
        return devices.DistinctBy(d => d.Id).ToArray();
    }

    private static void AddOtherDevices(List<GpuHardware> target, IEnumerable<GpuHardware> others)
    {
        foreach (var device in others)
        {
            // Keep distinct adapters. WMI/sysfs supplements devices not identified by nvidia-smi.
            if (device.Vendor == GpuVendor.Nvidia && target.Any(d => d.Vendor == GpuVendor.Nvidia && d.Name == device.Name)) continue;
            target.Add(device);
        }
    }

    internal static IReadOnlyList<GpuHardware> ParseNvidiaCsv(string value)
    {
        var devices = new List<GpuHardware>();
        foreach (var line in value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = Regex.Matches(line, "(?:^|,)\\s*(?:\"([^\"]*)\"|([^,]*))").Select(m =>
                m.Groups[1].Success ? m.Groups[1].Value.Trim() : m.Groups[2].Value.Trim()).ToArray();
            if (parts.Length != 6 || !parts[0].StartsWith("GPU-", StringComparison.Ordinal)) continue;
            long? memory = long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) ? amount : null;
            double? capability = double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var cc) ? cc : null;
            devices.Add(new GpuHardware(parts[0], parts[1], GpuVendor.Nvidia, parts[3], memory, capability, parts[2]));
        }
        return devices;
    }

    internal static IReadOnlyList<GpuHardware> ParseWindowsJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var rows = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToArray()
            : [document.RootElement];
        static string Get(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        return rows.Where(row => row.ValueKind == JsonValueKind.Object).Select(row =>
        {
            var name = Get(row, "Name"); var id = Get(row, "PNPDeviceID");
            return new GpuHardware(id, name, VendorFromText(id + " " + name), Get(row, "DriverVersion"));
        }).Where(d => !string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.Name)).ToArray();
    }

    private static IEnumerable<GpuHardware> ReadLinuxDevices(string pci)
    {
        if (!Directory.Exists("/sys/class/drm")) return [];
        var devices = new List<GpuHardware>();
        foreach (var card in Directory.EnumerateDirectories("/sys/class/drm").Where(p => Regex.IsMatch(Path.GetFileName(p), "^card[0-9]+$")))
        {
            var device = Path.Combine(card, "device");
            if (!File.Exists(Path.Combine(device, "vendor"))) continue;
            var id = new DirectoryInfo(device).ResolveLinkTarget(true)?.Name ?? Path.GetFileName(card);
            var vendor = VendorFromText(File.ReadAllText(Path.Combine(device, "vendor")));
            var line = pci.Split('\n').FirstOrDefault(l => l.StartsWith(id + " ", StringComparison.Ordinal)) ?? "";
            var quoted = Regex.Matches(line, "\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToArray();
            var name = quoted.Length >= 3 ? quoted[1] + " " + quoted[2] : $"{vendor} GPU · {id}";
            long? memory = File.Exists(Path.Combine(device, "mem_info_vram_total")) &&
                long.TryParse(File.ReadAllText(Path.Combine(device, "mem_info_vram_total")).Trim(), out var bytes) ? bytes / 1048576 : null;
            devices.Add(new GpuHardware(id, name, vendor, MemoryMiB: memory, PciAddress: id));
        }
        return devices;
    }

    private static GpuVendor VendorFromText(string text) => Regex.IsMatch(text, "10de|nvidia", RegexOptions.IgnoreCase) ? GpuVendor.Nvidia
        : Regex.IsMatch(text, "1002|advanced micro|amd|radeon", RegexOptions.IgnoreCase) ? GpuVendor.Amd
        : Regex.IsMatch(text, "8086|intel", RegexOptions.IgnoreCase) ? GpuVendor.Intel : GpuVendor.Unknown;

    public static async Task<IReadOnlyList<RuntimeDevice>> ListDevicesAsync(string executable, string backend, CancellationToken token)
    {
        var result = await RuntimeProcessProbe.RunAsync(executable, ["--list-devices"], TimeSpan.FromSeconds(15), token, true);
        if (result.ExitCode != 0) throw new ProviderException("runtime_probe_failed", "运行后端无法枚举设备，请检查驱动或选择 Vulkan／CPU。");
        return ParseDevices(result.Output + "\n" + result.Error, backend);
    }

    internal static IReadOnlyList<RuntimeDevice> ParseDevices(string text, string backend)
    {
        var result = new List<RuntimeDevice>();
        foreach (var line in text.Split('\n'))
        {
            var match = Regex.Match(line, @"^\s*(?<id>(?:CUDA|ROCm|Vulkan|HIP)\d+):\s*(?<name>.+?)\s*\((?<total>\d+)\s*MiB,\s*(?<free>\d+)\s*MiB\s+free\)", RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var id = match.Groups["id"].Value;
            if (backend.StartsWith("cuda", StringComparison.Ordinal) != id.StartsWith("CUDA", StringComparison.Ordinal)) continue;
            if (backend == "vulkan" && !id.StartsWith("Vulkan", StringComparison.Ordinal)) continue;
            if (backend == "rocm" && !(id.StartsWith("ROCm", StringComparison.Ordinal) || id.StartsWith("HIP", StringComparison.Ordinal))) continue;
            result.Add(new RuntimeDevice(backend, id, match.Groups["name"].Value,
                long.Parse(match.Groups["total"].Value, CultureInfo.InvariantCulture), long.Parse(match.Groups["free"].Value, CultureInfo.InvariantCulture)));
        }
        return result.DistinctBy(d => d.Id).ToArray();
    }
}

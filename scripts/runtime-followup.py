"""One-use reviewed integration corrections and documentation updates."""
from pathlib import Path


def edit(path, before, after, count=1):
    p = Path(path); text = p.read_text(encoding='utf-8')
    if text.count(before) != count:
        raise RuntimeError(f'{path}: expected {count}, got {text.count(before)}: {before[:80]}')
    p.write_text(text.replace(before, after), encoding='utf-8')


p = 'src/PingYi.Infrastructure/Runtime/RuntimeManager.cs'
# Span-based SequenceEqual cannot hold its receiver across the second await.
edit(p, ').SequenceEqual(await SHA256.HashDataAsync(b, token))', ').AsEnumerable().SequenceEqual(await SHA256.HashDataAsync(b, token))', 2)
edit(p, '    private async Task<InstalledRuntime> InstallBundleAsync(', '    internal async Task<InstalledRuntime> InstallBundleAsync(')
edit(p, '''                foreach (var file in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))''', '''                var stagedFiles = Directory.GetFiles(stage, "*", SearchOption.AllDirectories);
                if (stagedFiles.Length != Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length)
                    throw new InvalidDataException("Installed runtime has unexpected files.");
                foreach (var file in stagedFiles)''')
p = 'src/PingYi.Infrastructure/ManagedModelService.cs'
edit(p, '    private string? _runningExecutable;', '    private string? _runningExecutable;\n    private string? _runningInventory;')
edit(p, '            var selectedRuntime = await Runtimes.RecommendedInstalledAsync(normalizedBackend, deviceSelection, cancellationToken);', '''            var runtimeInventory = string.Join('|', ManagedRuntimeBackends.All.Where(b => b.Id != "auto")
                .Select(b => Runtimes.Find(b.Id)?.Executable ?? ""));''')
edit(p, '_runningExecutable == (selectedRuntime?.Executable ?? _runningExecutable)', '_runningInventory == runtimeInventory', 2)
edit(p, '                    _runningExecutable = runtime.ExecutablePath;', '                    _runningExecutable = runtime.ExecutablePath;\n                    _runningInventory = runtimeInventory;')
edit(p, '        _runningModelId = _runningBackendId = _runningExecutable = null;', '        _runningModelId = _runningBackendId = _runningExecutable = _runningInventory = null;')
p = 'src/PingYi.Core/RuntimeHardware.cs'
edit(p, '    public override string ToString() => $"{Id}', '''    public bool LikelySharedMemory => Regex.IsMatch(Name, @"AMD Radeon (?:\\(TM\\) )?Graphics|Intel.*(?:UHD|Iris|HD Graphics)", RegexOptions.IgnoreCase);
    public override string ToString() => $"{Id}''')
edit(p, 'devices.Where(d => d.IsHardwareGpu).OrderByDescending(d => d.FreeMiB)', 'devices.Where(d => d.IsHardwareGpu).OrderBy(d => d.LikelySharedMemory).ThenByDescending(d => d.FreeMiB)')
# Document actual scope without claiming real-GPU certification or a mainland network guarantee.
for name, text in {
    'README.md': '''\n## 显卡与运行后端（当前源码）\n\n首次引导和设置 → 本地模型新增显卡检测、执行显卡选择与后端下载安装。自动模式参考 NVIDIA 架构和驱动选择 CUDA 12／13，AMD 尝试 ROCm/HIP；保留 Vulkan／CPU。手动显卡或后端选择不会静默改用其他设备。显示的是当前后端实际枚举的设备，不使用系统显示器序号。\n\n点击下载／配置才联网查询官方稳定版及对应二进制；GitHub 不通时使用内置可信版本清单（明确不是已确认最新版）。可主动允许第三方备用下载源，连接／读数据超时后换源，始终校验官方文件大小与 SHA-256。不会安装显卡驱动，正常截图不检查更新。\n\n模型推理使用独立客户端，不再被通用 30 秒 HTTP 期限提前中断；主翻译和轻量回退使用独立期限。翻译失败后重试可复用本次截图的 OCR，识别设置变化或成功后重新处理则重新识别。基础模式与手动任务保持不变。\n\n这些是分支／源码能力，安装包以 Release 为准。支持条件、来源、实际验证与限制见 [运行后端说明](docs/RUNTIME_ACCELERATION.md)。\n''',
    'README.en.md': '''\n## GPU and inference runtimes (current source)\n\nFirst-run setup and Settings → Local models expose hardware detection, backend-native GPU selection and explicit runtime installation/update. Auto considers NVIDIA architecture/driver for CUDA 12/13 and probes AMD ROCm/HIP; Vulkan/CPU remain available. Manual choices do not silently switch to another device or backend.\n\nOnly an explicit download/configure action contacts upstream release metadata. An embedded trusted catalogue is used when GitHub metadata is unavailable, without calling it the latest. Optional third-party relays require opt-in; all executable archives must match official size and SHA-256. Drivers are not installed, and normal capture does not check runtime updates.\n\nInference no longer inherits the generic 30-second HTTP timeout. Primary translation and offline fallback have independent deadlines; failed translation retries can reuse session-only OCR. Installed-package availability depends on Releases. See [runtime details and validation limits](docs/RUNTIME_ACCELERATION.md).\n''',
    'THIRD_PARTY_NOTICES.md': '''\n## Optional hardware-aware runtime downloads\n\nThe runtime manager adds no production NuGet or Python dependency. The installer still bundles the existing Vulkan/CPU runtimes; CUDA/ROCm payloads are optional user-initiated downloads, not committed repository assets or installer payloads. llama.cpp is MIT; CUDA runtime libraries remain subject to NVIDIA redistribution/license terms and ROCm/HIP components to their upstream terms. Original archive files, including supplied licenses, are retained. Installation does not grant permission beyond those licenses and does not install drivers or the full development SDK.\n\nHardware probes use system tools and backend device enumeration. Optional ghfast.top/ghproxy.net relays are unaffiliated transports, not trusted hash authorities, and require user opt-in. Only official release API or embedded published digests authorize executable archives. No screenshots, recognized text, translation text or credentials are included in these requests. Sources: https://github.com/ggml-org/llama.cpp/releases, https://docs.nvidia.com/cuda/cuda-toolkit-release-notes/ and https://rocm.docs.amd.com/projects/ai-ecosystem/en/latest/inference/llamacpp.html .\n'''
}.items():
    p = Path(name); p.write_text(p.read_text(encoding='utf-8') + text, encoding='utf-8')
Path(__file__).unlink()

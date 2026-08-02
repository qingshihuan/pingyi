namespace PingYi.Core;

public sealed record ManagedRuntimeBackend(
    string Id,
    string DisplayName,
    string Description)
{
    public override string ToString() => DisplayName;
}

public static class ManagedRuntimeBackends
{
    public static ManagedRuntimeBackend Auto { get; } = new(
        "auto",
        "自动检测（推荐）",
        "优先使用 AMD、NVIDIA、Intel 均可用的 Vulkan，失败后自动回退 CPU。");

    public static ManagedRuntimeBackend Vulkan { get; } = new(
        "vulkan",
        "通用显卡 · Vulkan",
        "只使用 Vulkan 显卡后端，支持 AMD、NVIDIA 与 Intel；失败时不会回退 CPU。");

    public static ManagedRuntimeBackend Cpu { get; } = new(
        "cpu",
        "仅 CPU",
        "不使用独立显卡；速度较慢，但兼容性与可移植性最高。");

    public static IReadOnlyList<ManagedRuntimeBackend> All { get; } = [Auto, Vulkan, Cpu];

    public static string Normalize(string? id) => All.Any(candidate =>
        string.Equals(candidate.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase))
        ? id!.Trim().ToLowerInvariant()
        : Auto.Id;

    public static ManagedRuntimeBackend Get(string? id) => All.First(candidate =>
        string.Equals(candidate.Id, Normalize(id), StringComparison.OrdinalIgnoreCase));
}

public sealed record ManagedModelFile(
    string FileName,
    long Size,
    string Sha256);

public sealed record ManagedMultimodalModel(
    string Id,
    string DisplayName,
    string Summary,
    string ModelScopeRepository,
    string Revision,
    string ModelAlias,
    string Quantization,
    string License,
    string ReleaseDate,
    string HardwareHint,
    ManagedModelFile ModelFile,
    ManagedModelFile ProjectorFile,
    bool IsRecommended = false)
{
    public long TotalSize => ModelFile.Size + ProjectorFile.Size;

    public string ModelScopePageUrl => $"https://modelscope.cn/models/{ModelScopeRepository}";

    public Uri BuildDownloadUri(ManagedModelFile file) => new(
        $"https://modelscope.cn/models/{ModelScopeRepository}/resolve/{Revision}/{Uri.EscapeDataString(file.FileName)}");

    public override string ToString() => DisplayName;
}

public static class ManagedMultimodalModels
{
    public static IReadOnlyList<ManagedMultimodalModel> All { get; } =
    [
        new(
            "qwen35-2b-q4",
            "Qwen3.5 2B · Q4 平衡版（推荐）",
            "2026 新版原生多模态模型；OCR、翻译与多语言能力均衡。",
            "unsloth/Qwen3.5-2B-GGUF",
            "90057e31161eb95cc0bc1413c4f53b44de9b49c8",
            "pingyi-qwen3.5-2b-q4",
            "Q4_K_M + F16 mmproj",
            "Apache-2.0",
            "2026-03",
            "约 1.82 GiB；4 GB 显存可尝试，6 GB 更稳，也可纯 CPU 运行",
            new ManagedModelFile(
                "Qwen3.5-2B-Q4_K_M.gguf",
                1_280_835_840,
                "aaf42c8b7c3cab2bf3d69c355048d4a0ee9973d48f16c731c0520ee914699223"),
            new ManagedModelFile(
                "mmproj-F16.gguf",
                668_227_264,
                "7035e9cb8d7c6a9681d07eef9a364783e86ea4cd73faab2eabb4f43a101830c7"),
            true),
        new(
            "qwen35-2b-q8",
            "Qwen3.5 2B · Q8 质量版",
            "保留更高语言模型精度，适合更重视翻译和小字纠错的设备。",
            "unsloth/Qwen3.5-2B-GGUF",
            "90057e31161eb95cc0bc1413c4f53b44de9b49c8",
            "pingyi-qwen3.5-2b-q8",
            "Q8_0 + F16 mmproj",
            "Apache-2.0",
            "2026-03",
            "约 2.50 GiB；建议 6 GB 以上显存，也可纯 CPU 运行",
            new ManagedModelFile(
                "Qwen3.5-2B-Q8_0.gguf",
                2_012_012_800,
                "1b04acba824817554f4ce23639bc8495ff70453b8fcb047900c731521021f2c1"),
            new ManagedModelFile(
                "mmproj-F16.gguf",
                668_227_264,
                "7035e9cb8d7c6a9681d07eef9a364783e86ea4cd73faab2eabb4f43a101830c7")),
        new(
            "gemma4-e2b-q4",
            "Gemma 4 E2B · Q4 新模型版",
            "2026-06 发布，覆盖图像理解、OCR、翻译与 140+ 语言预训练。",
            "ggml-org/gemma-4-E2B-it-GGUF",
            "34e71d56791f98eab2930e45acf4e42132203d21",
            "pingyi-gemma-4-e2b-q4",
            "Q4_0 + Q8_0 mmproj",
            "Apache-2.0",
            "2026-06",
            "约 3.17 GiB；建议 8 GB 显存，低显存时自动回退 CPU",
            new ManagedModelFile(
                "gemma-4-E2B-it-Q4_0.gguf",
                2_841_481_184,
                "8e30dff3ac4c8434c49a7036fa15564bdbb6044e42bf04550bf1a096ad7e6a52"),
            new ManagedModelFile(
                "mmproj-gemma-4-E2B-it-Q8_0.gguf",
                557_368_064,
                "9406f99c16d68cda4f1f0552192dcc99021ea1fc6d2fd50b1dc3ccf30d04b292"))
    ];

    public static ManagedMultimodalModel Recommended => All.First(model => model.IsRecommended);

    public static bool TryGet(string? id, out ManagedMultimodalModel model)
    {
        model = All.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase))!;
        return model is not null;
    }
}

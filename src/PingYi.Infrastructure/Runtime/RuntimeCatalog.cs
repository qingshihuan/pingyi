using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using PingYi.Core;

namespace PingYi.Infrastructure;

public sealed record RuntimeAsset(string Tag, string Name, long Size, string Sha256, long AssetId = 0)
{
    public Uri OfficialUri => new($"https://github.com/ggml-org/llama.cpp/releases/download/{Tag}/{Name}");
    public void Validate()
    {
        if (!Regex.IsMatch(Tag, "^b[0-9]{4,9}$") || !Regex.IsMatch(Name, "^[a-zA-Z0-9._-]+\\.(zip|tar\\.gz)$") ||
            !Regex.IsMatch(Sha256, "^[a-fA-F0-9]{64}$") || Size is <= 0 or > 4_294_967_296)
            throw new InvalidDataException("Runtime asset metadata is invalid.");
    }
}

public sealed record RuntimeBundle(string Backend, string Tag, RuntimeAsset[] Assets, bool IsPinnedFallback = false)
{
    public long DownloadBytes => Assets.Sum(a => a.Size);
}

/// <summary>Trust GitHub's official release API for new digests; never take executable hashes from a relay.</summary>
public sealed class RuntimeCatalog(HttpClient client)
{
    public async Task<RuntimeBundle> ResolveAsync(string backend, bool windows, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var release = await GetJsonAsync("https://api.github.com/repos/ggml-org/llama.cpp/releases/latest", deadline.Token);
            var root = release.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Regex.IsMatch(tag, "^b[0-9]{4,9}$"))
            {
                var body = root.GetProperty("body").GetString() ?? "";
                var matches = Regex.Matches(body, @"https://github\.com/ggml-org/llama\.cpp/releases/tag/(b[0-9]{4,9})\b");
                var tags = matches.Select(m => m.Groups[1].Value).Distinct().ToArray();
                if (tags.Length != 1) throw new InvalidDataException("Stable release did not identify one binary build.");
                tag = tags[0];
                using var binaries = await GetJsonAsync("https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/" + tag, deadline.Token);
                return ParseOfficialRelease(binaries.RootElement, backend, windows);
            }
            return ParseOfficialRelease(root, backend, windows);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            return Pinned(backend, windows) with { IsPinnedFallback = true };
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ScreenInsight-RuntimeManager/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await RuntimeDownloader.ReadBoundedAsync(response.Content, 2 * 1024 * 1024, token));
    }

    internal static RuntimeBundle ParseOfficialRelease(JsonElement root, string backend, bool windows)
    {
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, "^b[0-9]{4,9}$") || root.GetProperty("draft").GetBoolean())
            throw new InvalidDataException("Invalid binary release.");
        var platform = windows ? "win" : "ubuntu";
        var suffix = windows ? "x64.zip" : "x64.tar.gz";
        var kind = backend switch
        {
            "cuda12" => @"cuda-12\.[0-9]+", "cuda13" => @"cuda-13\.[0-9]+", "rocm" => @"rocm-[0-9]+\.[0-9]+",
            "vulkan" => "vulkan", "cpu" => windows ? "cpu" : "", _ => throw new InvalidDataException("Unknown backend.")
        };
        var pattern = "^llama-" + tag + "-bin-" + platform + "-" + (kind.Length == 0 ? "" : kind + "-") + Regex.Escape(suffix) + "$";
        var all = root.GetProperty("assets").EnumerateArray().ToArray();
        var servers = all.Where(a => Regex.IsMatch(a.GetProperty("name").GetString() ?? "", pattern)).ToArray();
        if (servers.Length != 1) throw new InvalidDataException("No unambiguous compatible server asset.");
        var serverName = servers[0].GetProperty("name").GetString()!;
        var selected = new List<JsonElement> { servers[0] };
        if (backend is "cuda12" or "cuda13")
        {
            var cuda = Regex.Match(serverName, @"cuda-[0-9]+\.[0-9]+").Value;
            var runtimePattern = "^cudart-llama-(?:" + tag + "-)?bin-" + platform + "-" + Regex.Escape(cuda + "-" + suffix) + "$";
            var dependencies = all.Where(a => Regex.IsMatch(a.GetProperty("name").GetString() ?? "", runtimePattern)).ToArray();
            if (dependencies.Length != 1) throw new InvalidDataException("CUDA runtime dependencies are missing or ambiguous.");
            selected.Add(dependencies[0]);
        }
        var assets = selected.Select(a =>
        {
            var name = a.GetProperty("name").GetString()!;
            var digest = a.GetProperty("digest").GetString() ?? "";
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal)) throw new InvalidDataException("Missing upstream SHA-256.");
            var result = new RuntimeAsset(tag, name, a.GetProperty("size").GetInt64(), digest[7..], a.GetProperty("id").GetInt64());
            result.Validate();
            if (a.GetProperty("browser_download_url").GetString() != result.OfficialUri.AbsoluteUri ||
                a.GetProperty("state").GetString() != "uploaded") throw new InvalidDataException("Unexpected runtime source.");
            return result;
        }).ToArray();
        return new RuntimeBundle(backend, tag, assets);
    }

    public static RuntimeBundle Pinned(string backend, bool windows)
    {
        const string tag = "b11146"; // Official binary build associated with stable v0.5.0, verified 2026-09-29.
        RuntimeAsset Asset(string name, long size, string sha) => new(tag, name, size, sha);
        var assets = (windows, backend) switch
        {
            (true, "cuda12") => new[] {
                Asset("llama-b11146-bin-win-cuda-12.4-x64.zip", 253869799, "3c806a6ceccc3dae1c743ceb1a1fb2cce5b76f40bfbd4c6b7b8afb6ef45a5807"),
                Asset("cudart-llama-bin-win-cuda-12.4-x64.zip", 391443627, "8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6") },
            (true, "cuda13") => new[] {
                Asset("llama-b11146-bin-win-cuda-13.4-x64.zip", 149758833, "b1866c0ce76bc7bfb0c24b33e9a37e9669f1be18539b12c74ce361f81c41f047"),
                Asset("cudart-llama-bin-win-cuda-13.4-x64.zip", 423535356, "738f8c251ac22b70c3ae6f83a10cf222725df0395246a2cf58f32bdb85fbe668") },
            (false, "cuda12") => new[] {
                Asset("llama-b11146-bin-ubuntu-cuda-12.8-x64.tar.gz", 168920581, "c2ab9e19838513ff69d1af8d999ad717dd3c7ee4714ac04c7ed5ab9077c50e4e"),
                Asset("cudart-llama-b11146-bin-ubuntu-cuda-12.8-x64.tar.gz", 594373356, "1466daea60aad1144819e151b2bae19d54556cf1da6c129c4f55a5ded2637c25") },
            (false, "cuda13") => new[] {
                Asset("llama-b11146-bin-ubuntu-cuda-13.4-x64.tar.gz", 149265156, "1603d9c00a4b6eac8298c5c7868cdb080a3ac31948ab1e457441d71ce274dd7e"),
                Asset("cudart-llama-b11146-bin-ubuntu-cuda-13.4-x64.tar.gz", 440231388, "7c2af505f8b26ecd3707ab7723fa985fee1df233b7c1d60e5e17724b536d15bb") },
            (true, "rocm") => new[] { Asset("llama-b11146-bin-win-rocm-10.0-x64.zip", 251910679, "5dee283ec0fd5f38f29df0929769a07266ac6047f74381c153eb54b441e4ef99") },
            (false, "rocm") => new[] { Asset("llama-b11146-bin-ubuntu-rocm-10.0-x64.tar.gz", 234721151, "50e79dc559a11af3ea59391d416e9a704a715ac6be94352dbba710782c5dd7d1") },
            (true, "vulkan") => new[] { Asset("llama-b11146-bin-win-vulkan-x64.zip", 32127004, "55a378aa095b466979d85075234f66d7655c7a7483222af0c006c0e55b4d7bd6") },
            (false, "vulkan") => new[] { Asset("llama-b11146-bin-ubuntu-vulkan-x64.tar.gz", 30598492, "d3ce40fce7403cc93bcf5718fc46c6efb61ed9709f8e5d9f10c86bf0e30e8fb3") },
            (true, "cpu") => new[] { Asset("llama-b11146-bin-win-cpu-x64.zip", 18560055, "14cf1303ca9ac3abd94816850532f9f9a69ac66fbaca3776fc6f9061c2fac1d1") },
            (false, "cpu") => new[] { Asset("llama-b11146-bin-ubuntu-x64.tar.gz", 16998357, "c150306eb16b5ab696f76a8bdf810c35fd98a24e82158742e6fa28f420ff8410") },
            _ => throw new ProviderException("runtime_catalog_unavailable", "尚无此平台的可信离线运行包清单，请重试官方源或使用内置 Vulkan／CPU。")
        };
        foreach (var asset in assets) asset.Validate();
        return new RuntimeBundle(backend, tag, assets);
    }
}

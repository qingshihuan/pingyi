using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using PingYi.Core;

namespace PingYi.Infrastructure;

/// <summary>Relay servers are transport only. Exact size and an upstream digest are mandatory before extraction.</summary>
public sealed class RuntimeDownloader(HttpClient client, TimeSpan? headerTimeout = null, TimeSpan? idleTimeout = null)
{
    public static readonly string[] OptionalRelays = ["https://ghfast.top/", "https://ghproxy.net/"];

    public static IReadOnlyList<Uri> Sources(RuntimeAsset asset, bool allowRelays, IEnumerable<string>? customRelays = null)
    {
        asset.Validate();
        var sources = new List<Uri> { asset.OfficialUri };
        if (asset.AssetId > 0) sources.Add(new Uri($"https://api.github.com/repos/ggml-org/llama.cpp/releases/assets/{asset.AssetId}"));
        if (allowRelays)
        {
            foreach (var relay in (customRelays ?? []).Concat(OptionalRelays).Take(8))
            {
                if (!Uri.TryCreate(relay.Trim(), UriKind.Absolute, out var prefix) || !IsSafeHttps(prefix) ||
                    !string.IsNullOrEmpty(prefix.Query)) throw new ArgumentException("备用源必须是没有凭据或查询参数的 HTTPS 前缀。");
                sources.Add(new Uri(prefix.AbsoluteUri.TrimEnd('/') + "/" + asset.OfficialUri.AbsoluteUri));
            }
        }
        return sources.Distinct().ToArray();
    }

    public async Task DownloadAsync(RuntimeAsset asset, string destination, bool allowRelays,
        IProgress<ManagedModelProgress>? progress, CancellationToken token, IEnumerable<string>? customRelays = null)
    {
        asset.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        if (await VerifyAsync(destination, asset, token)) return;
        var partial = destination + ".partial";
        foreach (var source in Sources(asset, allowRelays, customRelays))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var length = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                if (length > asset.Size) { File.Delete(partial); length = 0; }
                if (length == asset.Size)
                {
                    if (await VerifyAsync(partial, asset, token)) { File.Move(partial, destination, true); return; }
                    File.Delete(partial); length = 0;
                }
                progress?.Report(new ManagedModelProgress("runtime-download", $"运行后端：{source.Host} · {asset.Name}", length, asset.Size));
                using var response = await OpenAsync(source, length, token);
                var append = length > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (range?.From != length || range.Length != asset.Size || range.To != asset.Size - 1)
                        throw new InvalidDataException("Download source returned an incompatible byte range.");
                }
                else if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("Unexpected download response.");
                if (!append) length = 0;
                if (response.Content.Headers.ContentLength is { } announced && announced != asset.Size - length)
                    throw new InvalidDataException("Runtime download length disagrees with trusted metadata.");
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using (var output = new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write,
                    FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[131072];
                    while (true)
                    {
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                        idle.CancelAfter(idleTimeout ?? TimeSpan.FromSeconds(15));
                        var read = await input.ReadAsync(buffer, idle.Token);
                        if (read == 0) break;
                        if (length + read > asset.Size) throw new InvalidDataException("Runtime download exceeded its expected size.");
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        length += read;
                        progress?.Report(new ManagedModelProgress("runtime-download", $"运行后端：{source.Host} · {asset.Name}", length, asset.Size));
                    }
                }
                if (!await VerifyAsync(partial, asset, token))
                {
                    if (File.Exists(partial) && new FileInfo(partial).Length == asset.Size) File.Delete(partial);
                    throw new InvalidDataException("Runtime download did not match its upstream size and SHA-256.");
                }
                File.Move(partial, destination, true);
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
            {
                progress?.Report(new ManagedModelProgress("runtime-mirror", $"{source.Host} 下载未完成，尝试下一来源；完整校验前不会执行。", 0, asset.Size, true));
            }
        }
        throw new ProviderException("runtime_download_failed", "所有允许的运行包下载源均失败。可重试续传、启用备用源或使用内置 Vulkan／CPU；未替换已有运行后端。");
    }

    private async Task<HttpResponseMessage> OpenAsync(Uri uri, long offset, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(headerTimeout ?? TimeSpan.FromSeconds(10));
        for (var redirects = 0; redirects < 6; redirects++)
        {
            if (!IsSafeHttps(uri)) throw new InvalidDataException("Runtime download refused an insecure redirect.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("ScreenInsight-RuntimeManager/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var next = response.Headers.Location;
                response.Dispose();
                if (next is null) throw new InvalidDataException("Missing download redirect.");
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                continue;
            }
            if (!response.IsSuccessStatusCode) { var status = response.StatusCode; response.Dispose(); throw new HttpRequestException("Runtime source returned HTTP " + (int)status); }
            return response;
        }
        throw new InvalidDataException("Too many runtime download redirects.");
    }

    private static bool IsSafeHttps(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && !uri.IsLoopback &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);

    public static async Task<bool> VerifyAsync(string path, RuntimeAsset asset, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != asset.Size) return false;
        await using var stream = File.OpenRead(path);
        var digest = await SHA256.HashDataAsync(stream, token);
        return Convert.ToHexString(digest).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken token)
    {
        await using var input = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("Response exceeded its size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}

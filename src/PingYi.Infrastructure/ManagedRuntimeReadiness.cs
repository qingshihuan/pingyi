using System.Net.Http;
using System.Text.Json;

namespace PingYi.Infrastructure;

/// <summary>One end-to-end loading deadline; individual HTTP timeouts are retryable.</summary>
public static class ManagedRuntimeReadiness
{
    public static TimeSpan VulkanTimeout { get; } = TimeSpan.FromMinutes(3);
    public static TimeSpan CpuTimeout { get; } = TimeSpan.FromMinutes(5);
    // Includes verification and the complete Auto sequence, not just its first backend.
    public static TimeSpan OperationTimeout { get; } = TimeSpan.FromMinutes(10);

    public static async Task WaitAsync(
        HttpClient client, Uri healthEndpoint, Uri modelsEndpoint, string expectedAlias,
        Func<int?> exitCode, TimeSpan timeout, CancellationToken cancellationToken,
        Action<TimeSpan>? progress = null, TimeProvider? clock = null,
        TimeSpan? probeTimeout = null, TimeSpan? pollInterval = null)
    {
        clock ??= TimeProvider.System;
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline = new CancellationTokenSource(timeout, clock);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var started = clock.GetTimestamp();
        var nextProgress = TimeSpan.Zero;
        try
        {
            while (true)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (exitCode() is { } code)
                    throw new InvalidOperationException($"llama.cpp 提前退出（代码 {code}）。请检查内存、运行后端及模型文件。");
                var elapsed = clock.GetElapsedTime(started);
                if (elapsed >= nextProgress)
                {
                    progress?.Invoke(elapsed);
                    nextProgress = elapsed + TimeSpan.FromSeconds(5);
                }
                using var probeDeadline = new CancellationTokenSource(probeTimeout ?? TimeSpan.FromSeconds(2), clock);
                using var probe = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, probeDeadline.Token);
                try
                {
                    using var health = await client.GetAsync(healthEndpoint, HttpCompletionOption.ResponseHeadersRead, probe.Token);
                    if (health.IsSuccessStatusCode)
                    {
                        using var status = await ReadSmallJsonAsync(health, probe.Token);
                        if (status.RootElement.ValueKind == JsonValueKind.Object &&
                            status.RootElement.TryGetProperty("status", out var state) &&
                            state.ValueKind == JsonValueKind.String && state.GetString() == "ok")
                        {
                            using var models = await client.GetAsync(modelsEndpoint, HttpCompletionOption.ResponseHeadersRead, probe.Token);
                            if (models.IsSuccessStatusCode)
                            {
                                using var body = await ReadSmallJsonAsync(models, probe.Token);
                                if (body.RootElement.ValueKind == JsonValueKind.Object &&
                                    body.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array &&
                                    data.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Object &&
                                        item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
                                        string.Equals(id.GetString(), expectedAlias, StringComparison.OrdinalIgnoreCase)))
                                    return;
                            }
                        }
                    }
                    // 503 means loading, not ready. Never infer readiness from /models alone.
                }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
                {
                    // A slow two-second probe is NOT the overall loading deadline.
                }
                catch (HttpRequestException) { }
                catch (JsonException) { }
                catch (IOException) { }
                await Task.Delay(pollInterval ?? TimeSpan.FromMilliseconds(500), clock, lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"llama.cpp 在 {timeout.TotalSeconds:0} 秒内未完成模型加载。可重试；内存不足时请选择较小模型或 CPU 后端。");
        }
    }

    private static async Task<JsonDocument> ReadSmallJsonAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, token)) != 0)
        {
            if (buffer.Length + read > 32 * 1024) throw new IOException("Model status response too large.");
            buffer.Write(chunk, 0, read);
        }
        return JsonDocument.Parse(buffer.ToArray());
    }
}

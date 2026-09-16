using System.Collections.Concurrent;

namespace PingYi.Infrastructure;

/// <summary>Drain native pipes with constant memory; retain only authored error categories.</summary>
public static class ManagedServerDiagnostics
{
    public static async Task DrainAsync(TextReader reader, ConcurrentQueue<string> messages)
    {
        var buffer = new char[4096];
        var tail = string.Empty;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory())) > 0)
            {
                var sample = tail + new string(buffer, 0, count);
                string? category = sample.Contains("out of memory", StringComparison.OrdinalIgnoreCase) ||
                                   sample.Contains("failed to allocate", StringComparison.OrdinalIgnoreCase)
                    ? "模型内存或显存分配失败，请选择较小模型或 CPU 后端。"
                    : sample.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
                        ? "本机模型端口已占用，请检查其他服务。"
                        : sample.Contains("VK_ERROR", StringComparison.OrdinalIgnoreCase)
                            ? "Vulkan 后端报告显卡错误，请检查驱动或切换 CPU。" : null;
                if (category is not null)
                {
                    messages.Enqueue(category);
                    while (messages.Count > 4) messages.TryDequeue(out _);
                }
                tail = sample.Length <= 64 ? sample : sample[^64..];
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException) { }
    }
}

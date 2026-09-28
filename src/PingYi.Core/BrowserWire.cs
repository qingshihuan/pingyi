using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace PingYi.Core;

// Native messaging and local IPC share a bounded, binary UTF-8 frame. Never log payloads.
public static class BrowserWire
{
    public const int MaxRequestBytes = 12 * 1024 * 1024;
    public const int MaxResponseBytes = 1024 * 1024 - 1;
    public static string PipeName(string edition) => "pingyi.browser.v1." + edition + "." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Environment.UserName + "|" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))))[..16];

    public static async Task<byte[]?> ReadAsync(Stream stream, int limit, CancellationToken token)
    {
        var header = new byte[4];
        if (await stream.ReadAsync(header.AsMemory(0, 1), token) == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > limit) throw new InvalidDataException("Invalid frame size.");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        return body;
    }

    public static async Task WriteAsync(Stream stream, byte[] body, int limit, CancellationToken token)
    {
        if (body.Length <= 0 || body.Length > limit) throw new InvalidDataException("Invalid frame size.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
        await stream.FlushAsync(token);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserRequest))]
[JsonSerializable(typeof(BrowserResponse))]
public partial class BrowserJsonContext : JsonSerializerContext;

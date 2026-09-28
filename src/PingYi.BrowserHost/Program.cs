using System.IO.Pipes;
using System.Text.Json;
using System.Text.RegularExpressions;
using PingYi.Core;

// Chrome validates allowed_origins against its registered native host manifest.
if (args.Length == 0 || !Regex.IsMatch(args[0], "^chrome-extension://[a-p]{32}/$")) return;
using var input = Console.OpenStandardInput();
using var output = Console.OpenStandardOutput();
while (true)
{
    byte[]? body;
    try { body = await BrowserWire.ReadAsync(input, BrowserWire.MaxRequestBytes, CancellationToken.None); }
    catch { break; }
    if (body is null) break;
    byte[] response;
    try
    {
        var request = JsonSerializer.Deserialize(body, BrowserJsonContext.Default.BrowserRequest);
        if (request is null || request.Edition is not ("standard" or "complete")) throw new InvalidDataException();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var pipe = new NamedPipeClientStream(".", BrowserWire.PipeName(request.Edition),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(2000, deadline.Token);
        await BrowserWire.WriteAsync(pipe, body, BrowserWire.MaxRequestBytes, deadline.Token);
        response = await BrowserWire.ReadAsync(pipe, BrowserWire.MaxResponseBytes, deadline.Token)
            ?? throw new IOException();
    }
    catch
    {
        response = JsonSerializer.SerializeToUtf8Bytes(new BrowserResponse(false, "desktop_unavailable"),
            BrowserJsonContext.Default.BrowserResponse);
    }
    try { await BrowserWire.WriteAsync(output, response, BrowserWire.MaxResponseBytes, CancellationToken.None); }
    catch { break; }
}

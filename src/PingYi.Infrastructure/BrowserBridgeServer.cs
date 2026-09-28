using System.IO.Pipes;
using System.Text.Json;
using PingYi.Core;

namespace PingYi.Infrastructure;

public sealed class BrowserBridgeServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task[] _listeners;

    public BrowserBridgeServer(string pipeName, Func<BrowserRequest, CancellationToken, Task<BrowserResponse>> handle)
    {
        // Fixed workers bound memory and inference queue size. ACL restricts clients to this user.
        _listeners = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 4,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    deadline.CancelAfter(TimeSpan.FromMinutes(5));
                    var bytes = await BrowserWire.ReadAsync(pipe, BrowserWire.MaxRequestBytes, deadline.Token);
                    if (bytes is null) continue;
                    BrowserResponse response;
                    try
                    {
                        var request = JsonSerializer.Deserialize(bytes, BrowserJsonContext.Default.BrowserRequest);
                        response = request is null ? new(false, "invalid_request") : await handle(request, deadline.Token);
                    }
                    catch (OperationCanceledException) { response = new(false, "request_timeout"); }
                    catch (Exception) { response = new(false, "provider_failed"); }
                    // Exceptions from providers may contain private text. Return only fixed error codes.
                    await BrowserWire.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(response,
                        BrowserJsonContext.Default.BrowserResponse), BrowserWire.MaxResponseBytes, deadline.Token);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    try { await Task.Delay(100, _stop.Token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        })).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await Task.WhenAll(_listeners);
        _stop.Dispose();
    }
}

using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace PingYi.App;

/// <summary>
/// Keeps one UI process per PingYi edition and forwards commands from later launches.
/// The IPC payload is deliberately limited to a small allow-list; no user content is sent.
/// </summary>
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private static readonly HashSet<string> AllowedCommands =
        new(StringComparer.OrdinalIgnoreCase) { "show", "settings", "capture" };

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentQueue<string> _pendingCommands = new();
    private readonly object _handlerLock = new();
    private Func<string, Task>? _handler;
    private Task? _listenerTask;

    private SingleInstanceCoordinator(Mutex mutex, string pipeName, bool isPrimary)
    {
        _mutex = mutex;
        _pipeName = pipeName;
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }

    public static SingleInstanceCoordinator Create(bool completeEdition)
    {
        var edition = completeEdition ? "complete" : "standard";
        var userScope = GetUserScope();
        var mutex = new Mutex(
            initiallyOwned: false,
            $"qingshihuan.pingyi.{edition}.{userScope}.singleton.v2");
        var ownsMutex = false;
        try
        {
            ownsMutex = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        return new SingleInstanceCoordinator(
            mutex,
            $"qingshihuan.pingyi.{edition}.{userScope}.ipc.v2",
            ownsMutex);
    }

    public void StartListening()
    {
        if (!IsPrimary || _listenerTask is not null)
        {
            return;
        }

        _listenerTask = Task.Run(ListenAsync);
    }

    public void SetHandler(Func<string, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_handlerLock)
        {
            _handler = handler;
        }

        while (_pendingCommands.TryDequeue(out var command))
        {
            _ = DispatchAsync(command);
        }
    }

    public async Task<bool> SendToPrimaryAsync(string command, CancellationToken cancellationToken = default)
    {
        command = NormalizeCommand(command);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linked.CancelAfter(TimeSpan.FromMilliseconds(500));
                await using var client = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(linked.Token);
                await using var writer = new StreamWriter(client, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
                await writer.WriteLineAsync(command.AsMemory(), linked.Token);
                await writer.FlushAsync(linked.Token);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The primary process may still be initializing its pipe listener.
            }
            catch (IOException)
            {
                // Retry briefly for the same startup race.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(125), cancellationToken);
        }

        return false;
    }

    public static string CommandFromArguments(IEnumerable<string> arguments)
    {
        var args = arguments.ToArray();
        if (args.Contains("--capture", StringComparer.OrdinalIgnoreCase))
        {
            return "capture";
        }

        return args.Contains("--settings", StringComparer.OrdinalIgnoreCase) ? "settings" : "show";
    }

    private async Task ListenAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_shutdown.Token);
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var command = await reader.ReadLineAsync(_shutdown.Token);
                if (command is not null)
                {
                    await DispatchAsync(NormalizeCommand(command));
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (IOException) when (!_shutdown.IsCancellationRequested)
            {
                await Task.Delay(100, _shutdown.Token);
            }
        }
    }

    private Task DispatchAsync(string command)
    {
        Func<string, Task>? handler;
        lock (_handlerLock)
        {
            handler = _handler;
        }

        if (handler is null)
        {
            _pendingCommands.Enqueue(command);
            return Task.CompletedTask;
        }

        _ = InvokeHandlerSafelyAsync(handler, command);
        return Task.CompletedTask;
    }

    private static async Task InvokeHandlerSafelyAsync(Func<string, Task> handler, string command)
    {
        try
        {
            await handler(command);
        }
        catch
        {
            // Command handlers surface recoverable errors in the existing UI.
            // IPC must remain available even if one command fails.
        }
    }

    private static string NormalizeCommand(string? command) =>
        command is not null && AllowedCommands.Contains(command.Trim())
            ? command.Trim().ToLowerInvariant()
            : "show";

    private static string GetUserScope()
    {
        var identity = $"{Environment.UserDomainName}\\{Environment.UserName}|{Environment.UserInteractive}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        if (IsPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The OS already released ownership during process teardown.
            }
        }

        _shutdown.Dispose();
        _mutex.Dispose();
    }
}

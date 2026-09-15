using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PingYi.Infrastructure;

public sealed class EngineProcessClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly AppDataPaths _paths;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _idleTimeout;
    private readonly Timer _idleTimer;
    private long _lastActivity;
    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeState;
    private Process? _process;
    private int _nextId;
    private bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

    public EngineProcessClient(AppDataPaths paths, TimeSpan? requestTimeout = null, TimeSpan? idleTimeout = null)
    {
        _paths = paths;
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(5);
        if (_idleTimeout != Timeout.InfiniteTimeSpan &&
            (_idleTimeout <= TimeSpan.Zero || _idleTimeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(idleTimeout));
        _requestTimeout = requestTimeout ?? TimeSpan.FromMinutes(2);
        if (_requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "The request timeout must be positive.");
        }
        // One timer per client; never wakes an unused engine and never polls.
        _idleTimer = new Timer(static state => _ = ((EngineProcessClient)state!).ReleaseIdleProcessAsync(),
            this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public async Task<JsonElement> CallAsync(
        string method,
        JsonObject? parameters = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        var effectiveTimeout = timeout ?? _requestTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The request timeout must be positive.");
        }
        requestCancellation.CancelAfter(effectiveTimeout);

        var enteredGate = false;
        var requestStarted = false;
        try
        {
            await _gate.WaitAsync(requestCancellation.Token);
            enteredGate = true;
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            EnsureStarted();
            var id = Interlocked.Increment(ref _nextId);
            var request = new JsonObject
            {
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters ?? new JsonObject()
            }.ToJsonString();
            requestStarted = true;
            await _process!.StandardInput.WriteLineAsync(request.AsMemory(), requestCancellation.Token);
            await _process.StandardInput.FlushAsync(requestCancellation.Token);

            while (true)
            {
                var line = await _process.StandardOutput.ReadLineAsync(requestCancellation.Token);
                if (line is null)
                {
                    var detail = _process.HasExited ? $"退出码 {_process.ExitCode}" : "输出已关闭";
                    ResetProcess(terminate: true);
                    throw new InvalidOperationException($"本地引擎意外停止：{detail}。");
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id)
                    {
                        continue;
                    }

                    if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                    {
                        var code = error.TryGetProperty("code", out var codeElement)
                            ? codeElement.GetString() ?? "engine_error"
                            : "engine_error";
                        var message = error.TryGetProperty("message", out var messageElement)
                            ? messageElement.GetString() ?? "本地引擎调用失败。"
                            : "本地引擎调用失败。";
                        throw new Core.ProviderException(code, message);
                    }

                    var result = root.GetProperty("result").Clone();
                    // Argos snapshots its package directory/translators at import.
                    // Recycle after successful mutations so the next request sees
                    // the new user package or bundled fallback, without restarting UI.
                    if (method is "install_translation_models" or "delete_models" or "shutdown")
                        ResetProcess(terminate: true);
                    return result;
                }
            }
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            !_disposeCancellation.IsCancellationRequested)
        {
            if (enteredGate && requestStarted)
            {
                ResetProcess(terminate: true);
            }

            throw new Core.ProviderException(
                "engine_timeout",
                $"本地引擎在 {effectiveTimeout.TotalSeconds:0} 秒内未响应。");
        }
        catch (OperationCanceledException)
        {
            // The engine host handles one request at a time. Terminating an in-flight request
            // prevents a canceled OCR/translation from delaying or replying into the next one.
            if (enteredGate && requestStarted)
            {
                ResetProcess(terminate: true);
            }

            throw;
        }
        finally
        {
            if (enteredGate)
            {
                if (!IsDisposed && _process is not null)
                {
                    _lastActivity = Stopwatch.GetTimestamp();
                    _idleTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
                }
                _gate.Release();
            }
        }
    }

    private void EnsureStarted()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        ResetProcess(); // Dispose the handle of an already-exited child before replacing it.
        var launch = ResolveLaunchCommand();
        var startInfo = new ProcessStartInfo(launch.FileName, launch.Arguments)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PINGYI_MODEL_DIR"] = _paths.ModelDirectory;
        startInfo.Environment["PINGYI_BUNDLED_MODEL_DIR"] = _paths.BundledModelDirectory;
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动屏译本地引擎。");
        _ = DrainStandardErrorAsync(_process);
    }

    private (string FileName, string Arguments) ResolveLaunchCommand()
    {
        var configured = Environment.GetEnvironmentVariable("PINGYI_ENGINE_HOST");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return (configured, string.Empty);
        }

        var executableName = OperatingSystem.IsWindows() ? "pingyi-engine.exe" : "pingyi-engine";
        var packaged = Path.Combine(AppContext.BaseDirectory, "engine-host", executableName);
        if (File.Exists(packaged))
        {
            return (packaged, string.Empty);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var script = Path.Combine(directory.FullName, "engine_host", "main.py");
            if (File.Exists(script))
            {
                var escaped = '"' + script.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
                var configuredPython = Environment.GetEnvironmentVariable("PINGYI_ENGINE_PYTHON");
                if (!string.IsNullOrWhiteSpace(configuredPython) && File.Exists(configuredPython))
                {
                    return (configuredPython, escaped);
                }

                var root = directory.FullName;
                var virtualEnvironmentPython = OperatingSystem.IsWindows()
                    ? Path.Combine(root, ".venv-engine", "Scripts", "python.exe")
                    : Path.Combine(root, ".venv-engine", "bin", "python");
                if (File.Exists(virtualEnvironmentPython))
                {
                    return (virtualEnvironmentPython, escaped);
                }

                return OperatingSystem.IsWindows()
                    ? ("py", $"-3 {escaped}")
                    : ("python3", escaped);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "未找到本地引擎。开发环境请保留 engine_host/main.py，发布环境请放置 engine-host/pingyi-engine。");
    }

    private static async Task DrainStandardErrorAsync(Process process)
    {
        try
        {
            var buffer = new char[1024];
            while (await process.StandardError.ReadAsync(buffer.AsMemory()) > 0)
            {
                // A dependency can emit an unbounded line. Drain in fixed-size
                // chunks, discard immediately, and never accumulate/log text.
                Array.Clear(buffer);
            }
        }
        catch
        {
            // Process shutdown races are harmless here.
        }
    }

    private void ResetProcess(bool terminate = false)
    {
        if (terminate && _process is { HasExited: false } process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may have exited between the state check and Kill.
            }
        }

        _process?.Dispose();
        _process = null;
    }

    private async Task ReleaseIdleProcessAsync()
    {
        // An in-flight request owns the process until it finishes. Its finally
        // block rearms the timer, so a busy/queued caller cannot be terminated.
        if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (IsDisposed || _process is null || _idleTimeout == Timeout.InfiniteTimeSpan) return;
            var remaining = _idleTimeout - Stopwatch.GetElapsedTime(_lastActivity);
            if (remaining > TimeSpan.Zero)
            {
                _idleTimer.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }
            ResetProcess(terminate: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // Shutdown races must not surface as unobserved timer exceptions.
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            await _disposeCompletion.Task.ConfigureAwait(false);
            return;
        }
        try
        {
            await _disposeCancellation.CancelAsync().ConfigureAwait(false);
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Disarm under the same gate used to rearm after requests.
                _idleTimer.Dispose();
                if (_process is { HasExited: false })
                {
                    try
                    {
                        await _process.StandardInput.WriteLineAsync("{\"id\":0,\"method\":\"shutdown\",\"params\":{}}");
                        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                    }
                    catch
                    {
                        // ResetProcess below handles exit/kill races.
                    }
                }
                ResetProcess(terminate: true);
            }
            finally { _gate.Release(); }
        }
        finally
        {
            Volatile.Write(ref _disposeState, 2);
            _disposeCompletion.TrySetResult();
        }
    }
}

using Avalonia.Threading;

namespace PingYi.App;

/// <summary>UI-thread refresh queue. Hidden/busy windows defer work; requests during a read are retained.</summary>
internal sealed class PassiveStatusUpdater : IDisposable
{
    private readonly Func<bool> _canRefresh;
    private readonly Func<CancellationToken, bool, Task> _refresh;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _busy, _requested, _force, _closed;
    private Task _work = Task.CompletedTask;

    internal PassiveStatusUpdater(Func<bool> canRefresh, Func<CancellationToken, bool, Task> refresh)
    {
        _canRefresh = canRefresh; _refresh = refresh;
        _timer.Tick += async (_, _) => await RequestAsync();
        _timer.Start();
    }

    internal Task RequestAsync(bool force = false)
    {
        if (_closed) return Task.CompletedTask;
        _requested = true; _force |= force;
        if (_busy || !_canRefresh()) return _work;
        _busy = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work = completion.Task;
        _ = DrainAsync(completion);
        return _work;
    }

    private async Task DrainAsync(TaskCompletionSource completion)
    {
        try
        {
            while (!_closed && _requested && _canRefresh())
            {
                var force = _force; _requested = _force = false;
                await _refresh(_lifetime.Token, force);
            }
            completion.TrySetResult();
        }
        catch (OperationCanceledException) when (_closed) { completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
        finally { _busy = false; }
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true; _timer.Stop(); _lifetime.Cancel();
        if (!_busy) _lifetime.Dispose();
        else _ = _work.ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default);
    }
}

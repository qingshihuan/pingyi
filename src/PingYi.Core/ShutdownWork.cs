namespace PingYi.Core;

/// <summary>Every cleanup is attempted, even when an earlier callback fails synchronously.</summary>
public static class ShutdownWork
{
    public static async Task RunAllAsync(params Func<Task>[] actions)
    {
        var results = await Task.WhenAll(actions.Select(AttemptAsync));
        var failures = results.OfType<Exception>().ToArray();
        if (failures.Length != 0) throw new AggregateException("Resource shutdown failed.", failures);
    }

    private static async Task<Exception?> AttemptAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception failure) { return failure; }
    }
}

/// <summary>Re-entrant exit requests share one completion, including its failure.</summary>
public sealed class ShutdownOnce
{
    private readonly object _sync = new();
    private TaskCompletionSource? _completion;

    public Task RunAsync(Func<Task> action)
    {
        TaskCompletionSource completion;
        lock (_sync)
        {
            if (_completion is not null) return _completion.Task;
            completion = _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        _ = CompleteAsync(action, completion);
        return completion.Task;
    }

    private static async Task CompleteAsync(Func<Task> action, TaskCompletionSource completion)
    {
        try { await action(); completion.TrySetResult(); }
        catch (Exception failure) { completion.TrySetException(failure); }
    }
}

namespace PingYi.Core;

/// <summary>Keep the previous backend until the replacement is verified and persisted.</summary>
public static class RuntimeChangeTransaction
{
    public static async Task ApplyAsync(Func<CancellationToken, Task> prepare,
        Func<CancellationToken, Task> activate, Func<CancellationToken, Task> persist,
        Func<Task> restore, CancellationToken token)
    {
        // Preparation may download and probe, but must not stop the existing server.
        await prepare(token);
        token.ThrowIfCancellationRequested();
        try
        {
            await activate(token);
            token.ThrowIfCancellationRequested();
            await persist(token);
        }
        catch (Exception failure)
        {
            // Caller supplies a separate bounded restoration token: the user's cancelled
            // installation token must not prevent restoring the previous configuration.
            try { await restore(); }
            catch (Exception recovery)
            {
                throw new AggregateException("切换失败，恢复原后端也未完成；原配置和文件仍保留。 / Runtime switch and recovery failed; previous settings and files are retained.", failure, recovery);
            }
            throw;
        }
    }
}

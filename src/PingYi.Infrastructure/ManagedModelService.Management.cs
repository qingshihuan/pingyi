using PingYi.Core;

namespace PingYi.Infrastructure;

public sealed partial class ManagedModelService
{
    public bool HasRunningOwnedBackend
    {
        get
        {
            try { return _ownedProcess is { HasExited: false }; }
            catch (InvalidOperationException) { return false; }
        }
    }

    public async Task<RuntimeRemovalResult> RemoveDownloadedRuntimeAsync(string backend, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _operationGate.WaitAsync(operation.Token);
        try
        {
            // Serialize with loading/stopping the owned server. Never unlink a running backend
            // and never kill an unrelated model server by process name.
            if (HasRunningOwnedBackend && (_runningBackendId is null || _runningBackendId == backend))
                throw new ProviderException("runtime_in_use", "该后端正在运行，请先安装并切换到其他后端，再卸载旧后端。 / Switch to another backend before uninstalling the active one.");
            return await Runtimes.RemoveDownloadedAsync(backend, operation.Token);
        }
        finally { _operationGate.Release(); }
    }
}

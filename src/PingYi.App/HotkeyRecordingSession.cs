using PingYi.Core;

namespace PingYi.App;

/// <summary>Temporarily release the application's grab so recording its own shortcut cannot capture.</summary>
internal sealed class HotkeyRecordingSession(IGlobalHotkeyService? service, string original)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public bool IsRecording { get; private set; }

    public async Task BeginAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (IsRecording) return;
            if (service is not null) await service.StopAsync();
            IsRecording = true;
        }
        finally { _gate.Release(); }
    }

    public async Task EndAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsRecording) return;
            IsRecording = false;
            if (service is not null) await service.StartAsync(original);
        }
        finally { _gate.Release(); }
    }
}

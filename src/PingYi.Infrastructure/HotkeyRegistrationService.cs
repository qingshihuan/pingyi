using PingYi.Core;

namespace PingYi.Infrastructure;

/// <summary>Expose registration failures separately from model readiness.</summary>
public sealed class HotkeyRegistrationService : IGlobalHotkeyService, IGlobalHotkeyStatus
{
    private readonly IGlobalHotkeyService inner;
    public HotkeyRegistrationService(IGlobalHotkeyService inner)
    {
        this.inner = inner;
        if (inner is IGlobalHotkeyStatus status) status.RegistrationChanged += OnInnerChanged;
    }
    private void OnInnerChanged(object? sender, EventArgs e)
    {
        var status = (IGlobalHotkeyStatus)inner;
        IsRegistered = status.IsRegistered;
        RegisteredShortcut = status.RegisteredShortcut;
        RegistrationError = status.RegistrationError;
        RegistrationChanged?.Invoke(this, EventArgs.Empty);
    }
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _starting;
    public bool IsRegistered { get; private set; }
    public string? RegisteredShortcut { get; private set; }
    public Exception? RegistrationError { get; private set; }
    public event EventHandler? RegistrationChanged;
    public event EventHandler? Pressed { add => inner.Pressed += value; remove => inner.Pressed -= value; }

    public async Task StartAsync(string shortcut, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        using var starting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _starting = starting;
        try
        {
            await inner.StartAsync(shortcut, starting.Token);
            if (inner is IGlobalHotkeyStatus status)
            {
                IsRegistered = status.IsRegistered;
                RegisteredShortcut = status.RegisteredShortcut;
                RegistrationError = status.RegistrationError;
            }
            else { IsRegistered = true; RegisteredShortcut = shortcut; RegistrationError = null; }
        }
        catch (Exception exception)
        {
            IsRegistered = false;
            RegisteredShortcut = null;
            RegistrationError = exception;
            throw;
        }
        finally
        {
            _starting = null;
            _gate.Release();
            RegistrationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try { _starting?.Cancel(); } catch (ObjectDisposedException) { }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await inner.StopAsync(cancellationToken);
            IsRegistered = false;
            RegisteredShortcut = null;
        }
        finally { _gate.Release(); RegistrationChanged?.Invoke(this, EventArgs.Empty); }
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (inner is IGlobalHotkeyStatus status) status.RegistrationChanged -= OnInnerChanged;
        await inner.DisposeAsync();
    }
}

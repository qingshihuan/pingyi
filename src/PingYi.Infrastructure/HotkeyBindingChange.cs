using PingYi.Core;

namespace PingYi.Infrastructure;

/// <summary>Apply a binding before persisting it; restore the previous binding on failure.</summary>
public static class HotkeyBindingChange
{
    // Callers serialize changes and recording sessions. The callbacks do not log user data.
    public static async Task ApplyAsync(IGlobalHotkeyService service, string previous, string next,
        Func<Task> persist, Action<Exception?> reportRegistrationError, bool retry = false)
    {
        var gesture = GlobalHotkeyGesture.Parse(next); // Validate before releasing a working binding.
        var changed = retry || !string.Equals(previous, gesture.ToString(), StringComparison.OrdinalIgnoreCase);
        if (!changed)
        {
            await persist();
            return;
        }
        try { await service.StopAsync(); }
        catch (Exception error) { reportRegistrationError(error); throw; }
        try
        {
            await service.StartAsync(gesture.ToString());
            await persist();
            reportRegistrationError(null);
        }
        catch
        {
            try
            {
                await service.StopAsync();
                await service.StartAsync(previous);
                reportRegistrationError(null);
            }
            catch (Exception rollbackError) { reportRegistrationError(rollbackError); }
            throw;
        }
    }

    /// <summary>Temporarily release our binding so recording it cannot trigger a capture.</summary>
    public static async Task<string?> RecordAsync(IGlobalHotkeyService service, string active,
        Func<Task<string?>> record, Action<Exception?> reportRegistrationError)
    {
        try { await service.StopAsync(); }
        catch (Exception error) { reportRegistrationError(error); throw; }
        try { return await record(); }
        finally
        {
            try { await service.StartAsync(active); reportRegistrationError(null); }
            catch (Exception error) { reportRegistrationError(error); }
        }
    }
}

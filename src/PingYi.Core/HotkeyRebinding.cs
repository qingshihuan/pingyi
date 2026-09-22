namespace PingYi.Core;

/// <summary>Register before persisting; a rejected binding or failed save restores the old binding.</summary>
public static class HotkeyRebinding
{
    public static async Task ApplyAsync(IGlobalHotkeyService service, string previous, string next,
        Func<Task> persist, Action<Exception?> reportRegistrationError)
    {
        next = HotkeyDefinition.Normalize(next); // Invalid input never unregisters a working shortcut.
        await service.StopAsync();
        try
        {
            await service.StartAsync(next);
            reportRegistrationError(null);
            await persist();
        }
        catch (Exception failure)
        {
            try
            {
                await service.StopAsync();
                await service.StartAsync(previous);
                reportRegistrationError(null);
            }
            catch (Exception rollbackFailure)
            {
                reportRegistrationError(rollbackFailure);
                throw new AggregateException("The shortcut change failed and the previous shortcut could not be restored.",
                    failure, rollbackFailure);
            }
            throw;
        }
    }
}

using SystemResourceMonitor.Desktop.Settings;

namespace SystemResourceMonitor.Desktop.Lifecycle;
/// <summary>Offers successfully applied edges to storage one at a time in selection order.</summary>
internal sealed class SettingsPersistenceQueue(
    ISettingsStore store,
    Action<string> reportStatus,
    Action<string>? record,
    Action<string, Task> track,
    Action<Action> dispatch)
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    public void Enqueue(BarEdge edge)
    {
        Task save;
        lock (_gate)
        {
            save = _tail = _tail.ContinueWith(
                _ => SaveOneAsync(edge),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default).Unwrap();
        }

        track($"settings-save-{edge}", save);
    }

    private async Task SaveOneAsync(BarEdge edge)
    {
        SettingsSaveResult result;
        try
        {
            result = await store.SaveAsync(new BarSettings(edge)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            dispatch(() =>
            {
                record?.Invoke($"settings-save-failed:{exception.GetType().Name}");
                record?.Invoke("settings-save-restart-only");
                reportStatus("The current position applies only until restart.");
            });
            return;
        }

        if (result.Succeeded)
        {
            dispatch(() => record?.Invoke($"settings-saved:{edge}"));
            return;
        }

        dispatch(() =>
        {
            record?.Invoke($"settings-save-failed:{result.Issue}");
            record?.Invoke("settings-save-restart-only");
            reportStatus("The current position applies only until restart.");
        });
    }
}

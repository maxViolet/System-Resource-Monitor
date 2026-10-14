using System.Collections.Concurrent;
using SystemResourceMonitor.Desktop.Lifecycle;
using SystemResourceMonitor.Desktop.Settings;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class SettingsPersistenceQueueTests
{
    [Fact]
    public async Task Rapid_edges_are_offered_to_storage_one_at_a_time_in_selection_order()
    {
        var store = new ControlledStore();
        var tracked = new List<Task>();
        var queue = new SettingsPersistenceQueue(store, _ => { }, null, (_, task) => tracked.Add(task), action => action());

        queue.Enqueue(BarEdge.Top);
        queue.Enqueue(BarEdge.Bottom);

        Assert.Equal(BarEdge.Top, await store.NextOfferAsync(TestContext.Current.CancellationToken));
        Assert.False(store.TryTakeOffer(out _));
        store.CompleteNext(new SettingsSaveResult());
        Assert.Equal(BarEdge.Bottom, await store.NextOfferAsync(TestContext.Current.CancellationToken));
        store.CompleteNext(new SettingsSaveResult());
        await Task.WhenAll(tracked).WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Write_failure_keeps_the_live_choice_and_reports_the_restart_only_limitation()
    {
        var statuses = new List<string>();
        var markers = new List<string>();
        Task? tracked = null;
        var queue = new SettingsPersistenceQueue(
            new FailingStore(),
            statuses.Add,
            markers.Add,
            (_, task) => tracked = task,
            action => action());

        queue.Enqueue(BarEdge.Bottom);
        await tracked!.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["The current position applies only until restart."], statuses);
        Assert.Equal(["settings-save-failed:WriteFailed", "settings-save-restart-only"], markers);
    }

    private sealed class ControlledStore : ISettingsStore
    {
        private readonly ConcurrentQueue<BarEdge> _offers = new();
        private readonly ConcurrentQueue<TaskCompletionSource<SettingsSaveResult>> _completions = new();
        private readonly SemaphoreSlim _available = new(0);

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsLoadResult(BarSettings.Default));

        public Task<SettingsSaveResult> SaveAsync(BarSettings settings, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<SettingsSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _offers.Enqueue(settings.Edge);
            _completions.Enqueue(completion);
            _available.Release();
            return completion.Task;
        }

        public async Task<BarEdge> NextOfferAsync(CancellationToken cancellationToken)
        {
            await _available.WaitAsync(cancellationToken);
            return _offers.TryDequeue(out var edge) ? edge : throw new InvalidOperationException("Missing offered edge.");
        }

        public bool TryTakeOffer(out BarEdge edge) => _offers.TryDequeue(out edge);

        public void CompleteNext(SettingsSaveResult result)
        {
            if (!_completions.TryDequeue(out var completion))
            {
                throw new InvalidOperationException("Missing save completion.");
            }

            completion.SetResult(result);
        }
    }

    private sealed class FailingStore : ISettingsStore
    {
        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsLoadResult(BarSettings.Default));

        public Task<SettingsSaveResult> SaveAsync(BarSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsSaveResult(SettingsSaveIssue.WriteFailed));
    }
}

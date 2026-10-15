using Microsoft.Extensions.Time.Testing;
using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Lifecycle;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class MetricRunFailureTests
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_faulted_monitor_run_enters_failure_shutdown()
    {
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime = new CancellationTokenSource();
        var shutdown = new ShutdownCoordinator(lifetime, new FakeTimeProvider(), exited.SetResult);
        var monitor = new MetricMonitor(new SteadySource(MetricKind.Cpu), new SteadySource(MetricKind.Ram), TimeProvider.System);
        monitor.Changed += (_, _) => throw new InvalidOperationException("Injected handler failure.");
        var posted = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? failure = null;

        var run = monitor.RunAsync(lifetime.Token);
        MetricRunFailure.Observe(run, action => posted.SetResult(action), exception =>
        {
            failure = exception;
            _ = shutdown.ShutdownAsync(ExitCodes.Failure);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(CompletionTimeout, TestContext.Current.CancellationToken));
        var post = await posted.Task.WaitAsync(CompletionTimeout, TestContext.Current.CancellationToken);

        // The failure runs on the posting target, never on the monitor's thread.
        Assert.Null(failure);
        post();
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(ExitCodes.Failure, await exited.Task.WaitAsync(CompletionTimeout, TestContext.Current.CancellationToken));
        Assert.True(lifetime.IsCancellationRequested);
    }

    [Fact]
    public async Task A_cancelled_monitor_run_is_not_a_failure()
    {
        using var lifetime = new CancellationTokenSource();
        var monitor = new MetricMonitor(new SteadySource(MetricKind.Cpu), new SteadySource(MetricKind.Ram), TimeProvider.System);
        var posted = new List<Action>();

        var run = monitor.RunAsync(lifetime.Token);
        MetricRunFailure.Observe(run, posted.Add, _ => Assert.Fail("cancellation was treated as a failure"));
        await lifetime.CancelAsync();
        await run.WaitAsync(CompletionTimeout, TestContext.Current.CancellationToken);

        Assert.Empty(posted);
    }

    private sealed class SteadySource(MetricKind kind) : IMetricSource
    {
        public MetricKind Kind => kind;

        public MetricSample Read() => kind == MetricKind.Cpu ? new MeasuredSample(12) : new MeasuredRamSample(1, 2);
    }
}

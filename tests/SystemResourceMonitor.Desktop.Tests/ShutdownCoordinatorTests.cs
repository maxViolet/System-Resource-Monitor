using Microsoft.Extensions.Time.Testing;
using SystemResourceMonitor.Desktop.Lifecycle;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class ShutdownCoordinatorTests
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);

    private readonly List<string> _events = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ShutdownCoordinator _shutdown;

    public ShutdownCoordinatorTests()
    {
        _lifetime.Token.Register(() => _events.Add("cancel"));
        _shutdown = new ShutdownCoordinator(_lifetime, _time, code => _events.Add($"exited:{code}"), _events.Add);
    }

    [Fact]
    public void AppBar_is_removed_before_the_request_returns_and_before_the_wait()
    {
        _shutdown.Attach(new FakeBar(_events));
        _shutdown.Track("source", new TaskCompletionSource().Task);

        var completion = _shutdown.ShutdownAsync(ExitCodes.Success);

        Assert.Equal(["cancel", "remove-appbar", "remove", "remove-tray", "tray-remove", "close-final", "wait-start"], _events);
        Assert.False(completion.IsCompleted);
        Assert.False(_shutdown.TryPublish(() => _events.Add("published")));
    }

    [Fact]
    public async Task Pending_work_is_recorded_at_the_deadline_and_its_late_result_cannot_publish()
    {
        _shutdown.Attach(new FakeBar(_events));
        var source = new TaskCompletionSource();
        _shutdown.Track("source", source.Task);

        var completion = _shutdown.ShutdownAsync(ExitCodes.Success);
        _time.Advance(ShutdownCoordinator.WaitDeadline - TimeSpan.FromTicks(1));
        Assert.False(completion.IsCompleted);

        _time.Advance(TimeSpan.FromTicks(1));
        await completion.WaitAsync(CompletionTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(["wait-start", "pending:source", "exit", "exited:0"], _events[^4..]);
        source.SetResult();
        Assert.False(_shutdown.TryPublish(() => _events.Add("published")));
        Assert.DoesNotContain("published", _events);
    }

    [Fact]
    public async Task Work_that_finishes_before_the_deadline_ends_the_wait_even_when_it_faults()
    {
        _shutdown.Attach(new FakeBar(_events));
        var source = new TaskCompletionSource();
        _shutdown.Track("source", source.Task);

        var completion = _shutdown.ShutdownAsync(ExitCodes.Success);
        source.SetException(new InvalidOperationException("collector failed"));
        await completion.WaitAsync(CompletionTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(["wait-start", "exit", "exited:0"], _events[^3..]);
    }

    [Fact]
    public void Repeated_requests_shut_down_once_with_the_first_exit_code()
    {
        _shutdown.Attach(new FakeBar(_events));

        var first = _shutdown.ShutdownAsync(ExitCodes.Failure);
        var second = _shutdown.ShutdownAsync(ExitCodes.Success);

        Assert.Same(first, second);
        Assert.True(first.IsCompleted);
        Assert.Equal(
            ["cancel", "remove-appbar", "remove", "remove-tray", "tray-remove", "close-final", "wait-start", "exit", "exited:1"],
            _events);
    }

    [Fact]
    public void Failure_before_the_bar_exists_exits_without_touching_a_bar()
    {
        var completion = _shutdown.ShutdownAsync(ExitCodes.Failure);

        Assert.True(completion.IsCompleted);
        Assert.Equal(["cancel", "wait-start", "exit", "exited:1"], _events);
    }

    [Fact]
    public async Task Shutdown_continues_through_removal_close_and_wait_when_cancellation_throws()
    {
        _lifetime.Token.Register(() => throw new InvalidOperationException("callback failed"));
        _shutdown.Attach(new FakeBar(_events));
        _shutdown.Track("source", new TaskCompletionSource().Task);

        var completion = _shutdown.ShutdownAsync(ExitCodes.Success);
        _time.Advance(ShutdownCoordinator.WaitDeadline);
        await completion.WaitAsync(CompletionTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "cancel", "shutdown-failed:AggregateException", "remove-appbar", "remove", "remove-tray", "tray-remove",
                "close-final", "wait-start", "pending:source", "exit", $"exited:{ExitCodes.Failure}",
            ],
            _events);
    }

    [Fact]
    public void Rejected_AppBar_removal_is_reported_and_shutdown_still_closes_and_exits()
    {
        _shutdown.Attach(new FakeBar(_events, removalFails: true));

        _shutdown.ShutdownAsync(ExitCodes.Success);

        Assert.Equal(
            [
                "cancel", "remove-appbar", "shutdown-failed:InvalidOperationException", "remove-tray", "tray-remove",
                "close-final", "wait-start", "exit", $"exited:{ExitCodes.Failure}",
            ],
            _events);
    }

    private sealed class FakeBar(List<string> events, bool removalFails = false) : IShutdownBar
    {
        public void RemoveAppBar()
        {
            events.Add("remove-appbar");
            if (removalFails)
            {
                throw new InvalidOperationException("ABM_REMOVE failed.");
            }
        }

        public void RemoveTrayIcon() => events.Add("remove-tray");

        public void CloseFinal() => events.Add("close-final");
    }
}

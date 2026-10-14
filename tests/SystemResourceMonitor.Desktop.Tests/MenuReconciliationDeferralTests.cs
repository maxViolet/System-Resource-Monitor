using SystemResourceMonitor.Desktop.Bar;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class MenuReconciliationDeferralTests
{
    [Fact]
    public void Recovery_requested_while_the_menu_is_open_runs_once_after_it_closes_with_every_reason()
    {
        var menuOpen = false;
        var scheduled = new Queue<Action>();
        var passes = new List<ReconciliationReason>();
        MenuReconciliationDeferral? deferral = null;
        var coordinator = new ReconciliationCoordinator(scheduled.Enqueue, reason =>
        {
            if (!deferral!.TryDefer(reason))
            {
                passes.Add(reason);
            }
        });
        deferral = new MenuReconciliationDeferral(() => menuOpen, coordinator.Request);

        menuOpen = true;
        coordinator.Request(ReconciliationReason.ExplorerRestart);
        scheduled.Dequeue()();
        coordinator.Request(ReconciliationReason.Placement);
        scheduled.Dequeue()();
        Assert.Empty(passes);

        menuOpen = false;
        deferral.MenuClosed();
        scheduled.Dequeue()();

        Assert.Equal([ReconciliationReason.ExplorerRestart | ReconciliationReason.Placement], passes);
        Assert.Empty(scheduled);
    }

    [Fact]
    public void Closing_a_menu_with_nothing_deferred_schedules_no_pass()
    {
        var scheduled = new Queue<Action>();
        var coordinator = new ReconciliationCoordinator(scheduled.Enqueue, _ => { });
        var deferral = new MenuReconciliationDeferral(() => false, coordinator.Request);

        deferral.MenuClosed();

        Assert.Empty(scheduled);
    }
}

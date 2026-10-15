using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Lifecycle;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class MetricRelayTests
{
    private static readonly DashboardState First = DashboardState.Initial.With(
        new UnavailableMetric(MetricKind.Cpu, UnavailabilityReason.ReadFailed));

    private static readonly DashboardState Second = First.With(
        new UnavailableMetric(MetricKind.Ram, UnavailabilityReason.Invalid));

    [Fact]
    public void A_change_posts_once_and_applies_the_latest_state()
    {
        var posted = new List<Action>();
        var applied = new List<DashboardState>();
        var relay = new MetricRelay(posted.Add, applied.Add);

        relay.OnChanged(First);
        relay.OnChanged(Second);

        var post = Assert.Single(posted);
        post();
        Assert.Equal([Second], applied);
    }

    [Fact]
    public void A_change_after_the_post_ran_posts_again()
    {
        var posted = new Queue<Action>();
        var applied = new List<DashboardState>();
        var relay = new MetricRelay(posted.Enqueue, applied.Add);

        relay.OnChanged(First);
        posted.Dequeue()();
        relay.OnChanged(Second);
        posted.Dequeue()();

        Assert.Equal([First, Second], applied);
        Assert.Empty(posted);
    }

    [Fact]
    public void A_change_while_the_post_applies_is_not_lost()
    {
        var posted = new Queue<Action>();
        var applied = new List<DashboardState>();
        MetricRelay? relay = null;
        relay = new MetricRelay(posted.Enqueue, state =>
        {
            applied.Add(state);
            if (state == First)
            {
                relay!.OnChanged(Second);
            }
        });

        relay.OnChanged(First);
        while (posted.Count > 0)
        {
            posted.Dequeue()();
        }

        Assert.Equal([First, Second], applied);
    }
}

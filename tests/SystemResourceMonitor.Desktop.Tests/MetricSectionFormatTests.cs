using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Bar;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class MetricSectionFormatTests
{
    private static readonly DateTimeOffset MeasuredAt = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "CPU 0%")]
    [InlineData(100, "CPU 100%")]
    [InlineData(0.5, "CPU 1%")]
    [InlineData(0.49, "CPU 0%")]
    [InlineData(99.5, "CPU 100%")]
    [InlineData(99.49, "CPU 99%")]
    [InlineData(12.4, "CPU 12%")]
    public void Available_cpu_is_rounded_half_away_from_zero_without_a_tooltip(double percentage, string expected)
    {
        var section = MetricSectionFormat.Format(Cpu(percentage));

        Assert.Equal(expected, section.Text);
        Assert.Null(section.ToolTip);
    }

    [Theory]
    [InlineData(0UL, 1UL << 30, "RAM 0%", "RAM 0.0 / 1.0 GiB used")]
    [InlineData(1UL << 30, 1UL << 30, "RAM 100%", "RAM 1.0 / 1.0 GiB used")]
    [InlineData(1UL << 29, 1UL << 31, "RAM 25%", "RAM 0.5 / 2.0 GiB used")]
    [InlineData(10_995_116_278UL, 17_179_869_184UL, "RAM 64%", "RAM 10.2 / 16.0 GiB used")]
    public void Available_ram_shows_its_percentage_and_used_and_total_gib(ulong used, ulong total, string text, string toolTip)
    {
        var section = MetricSectionFormat.Format(Ram(used, total));

        Assert.Equal(text, section.Text);
        Assert.Equal(toolTip, section.ToolTip);
    }

    [Theory]
    [InlineData(UnavailabilityReason.NotYetSampled, "Waiting for the first measurement.")]
    [InlineData(UnavailabilityReason.Expired, "No update for 6 seconds.")]
    [InlineData(UnavailabilityReason.Invalid, "Windows returned an inconsistent measurement.")]
    [InlineData(UnavailabilityReason.ReadFailed, "Windows did not return a measurement.")]
    public void Every_unavailable_reason_shows_a_dash_and_a_safe_explanation(UnavailabilityReason reason, string toolTip)
    {
        var cpu = MetricSectionFormat.Format(new UnavailableMetric(MetricKind.Cpu, reason));
        var ram = MetricSectionFormat.Format(new UnavailableMetric(MetricKind.Ram, reason));

        Assert.Equal("CPU —", cpu.Text);
        Assert.Equal("RAM —", ram.Text);
        Assert.Equal(toolTip, cpu.ToolTip);
        Assert.Equal(toolTip, ram.ToolTip);
    }

    [Fact]
    public void Undefined_reason_is_a_programmer_error()
    {
        // UnavailableMetric already rejects an undefined reason, so test the formatter's mapping directly.
        Assert.Throws<ArgumentOutOfRangeException>(() => MetricSectionFormat.Explain((UnavailabilityReason)99));
    }

    [Fact]
    public void Gpu_formatting_shows_adapter_and_safe_unavailable_explanations()
    {
        var adapter = new GpuAdapter(new string('X', 70), new AdapterLuid(1, 2));
        var measured = MetricSectionFormat.Format(
            AvailableMetric.Create(MetricKind.Gpu, new MeasuredGpuSample(adapter, 37.5), MeasuredAt, 0), adapter);
        Assert.Equal("GPU 38%", measured.Text);
        Assert.Equal(new string('X', 64), measured.ToolTip);

        Assert.Equal("No discrete GPU found.", MetricSectionFormat.Format(
            new UnavailableMetric(MetricKind.Gpu, UnavailabilityReason.NoDiscreteAdapter)).ToolTip);
        Assert.Equal("More than one discrete GPU found; none is chosen.", MetricSectionFormat.Format(
            new UnavailableMetric(MetricKind.Gpu, UnavailabilityReason.AmbiguousAdapter)).ToolTip);
        Assert.Equal(new string('X', 64) + ": GPU counters could not be matched to this adapter.", MetricSectionFormat.Format(
            new UnavailableMetric(MetricKind.Gpu, UnavailabilityReason.Uncorrelated), adapter).ToolTip);
        Assert.Equal(new string('X', 64) + ": Windows did not return a measurement.", MetricSectionFormat.Format(
            new UnavailableMetric(MetricKind.Gpu, UnavailabilityReason.ReadFailed), adapter).ToolTip);
        Assert.Equal("GPU \u2014", MetricSectionFormat.Format(
            new UnavailableMetric(MetricKind.Gpu, UnavailabilityReason.ReadFailed)).Text);
    }

    [Fact]
    public void Undefined_kind_is_a_programmer_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MetricSectionFormat.WidthCandidates((MetricKind)99).ToList());
    }

    [Fact]
    public void Formatting_does_not_depend_on_the_current_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Assert.Equal("RAM 0.5 / 2.0 GiB used", MetricSectionFormat.Format(Ram(1UL << 29, 1UL << 31)).ToolTip);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Label_candidates_cover_every_percentage_and_the_dash()
    {
        var candidates = MetricSectionFormat.WidthCandidates(MetricKind.Ram).ToList();

        Assert.Equal(102, candidates.Count);
        Assert.Contains("RAM 0%", candidates);
        Assert.Contains("RAM 100%", candidates);
        Assert.Contains("RAM —", candidates);
    }

    private static MetricReading Cpu(double percentage) =>
        AvailableMetric.Create(MetricKind.Cpu, new MeasuredSample(percentage), MeasuredAt, 0);

    private static MetricReading Ram(ulong used, ulong total) =>
        AvailableMetric.Create(MetricKind.Ram, new MeasuredRamSample(used, total), MeasuredAt, 0);
}

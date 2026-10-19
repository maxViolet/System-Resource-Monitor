using SystemResourceMonitor.Core.Metrics;

namespace SystemResourceMonitor.Core.Tests.Metrics;

public sealed class GpuLoadCalculatorTests
{
    private static readonly GpuAdapter Selected = new("Discrete", new AdapterLuid(0x89abcdef, unchecked((int)0x81234567)));

    [Fact]
    public void Selector_requires_exactly_one_hardware_non_integrated_adapter()
    {
        var integrated = new GpuAdapterCandidate("iGPU", default, true, true);
        var software = new GpuAdapterCandidate("software", default, false, false);
        var discrete = new GpuAdapterCandidate("dGPU", Selected.Luid, true, false);

        Assert.Equal(UnavailabilityReason.NoDiscreteAdapter, GpuAdapterSelector.Select([integrated, software]).Reason);
        Assert.Equal(new GpuAdapter("dGPU", Selected.Luid), GpuAdapterSelector.Select([integrated, discrete]).Adapter);
        Assert.Equal(UnavailabilityReason.AmbiguousAdapter, GpuAdapterSelector.Select([discrete, discrete]).Reason);
    }

    [Fact]
    public void Parser_preserves_high_then_low_luid_parts_and_classifies_engine_names()
    {
        Assert.True(GpuEngineInstance.TryParse(Name("3D"), out var parsed));
        Assert.Equal(Selected.Luid, parsed.Luid);
        Assert.Equal(GpuEngineType.ThreeD, parsed.Engine);
        Assert.Equal(2, parsed.EngineIndex);
        Assert.True(GpuEngineInstance.TryParse(Name("VideoDecode"), out parsed));
        Assert.Equal(GpuEngineType.Video, parsed.Engine);
        Assert.True(GpuEngineInstance.TryParse(Name("Copy"), out parsed));
        Assert.Equal(GpuEngineType.Copy, parsed.Engine);
        Assert.True(GpuEngineInstance.TryParse(Name("compute_0"), out parsed));
        Assert.Equal(GpuEngineType.Compute, parsed.Engine);
        Assert.True(GpuEngineInstance.TryParse(Name("OtherEngine"), out parsed));
        Assert.Equal(GpuEngineType.Other, parsed.Engine);
        Assert.True(GpuEngineInstance.TryParse(Name(""), out parsed));
        Assert.Equal(GpuEngineType.Unspecified, parsed.Engine);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pid_x_luid_0x81234567_0x89abcdef_phys_0_eng_0_engtype_3D")]
    [InlineData("pid_1_luid_0x123_0x89abcdef_phys_0_eng_0_engtype_3D")]
    [InlineData("pid_1_luid_0x81234567_0x89abcdef_phys_x_eng_0_engtype_3D")]
    [InlineData("pid_1_luid_0x81234567_0x89abcdef_phys_0_eng_x_engtype_3D")]
    public void Parser_rejects_malformed_names(string name) => Assert.False(GpuEngineInstance.TryParse(name, out _));

    [Fact]
    public void Calculator_ignores_other_luids_and_returns_maximum_capped_engine_sum()
    {
        var items = new[]
        {
            new GpuCounterItem(Name("3D", process: 1), 70, true),
            new GpuCounterItem(Name("3D", process: 2), 60, true),
            new GpuCounterItem(Name("Compute", process: 1), 45, true),
            new GpuCounterItem("pid_1_luid_0x00000000_0x00000001_phys_0_eng_1_engtype_3D", 100, true),
        };

        var sample = Assert.IsType<MeasuredGpuSample>(GpuLoadCalculator.Calculate(Selected, items));
        Assert.Equal(Selected, sample.Adapter);
        Assert.Equal(100, sample.Percentage);
    }

    [Fact]
    public void Calculator_ignores_structurally_valid_empty_engine_from_foreign_luid()
    {
        var items = new GpuCounterItem[]
        {
            new("pid_1_luid_0x00000000_0x00000001_phys_0_eng_1_engtype_", 0, true),
            new(Name("3D"), 27, true),
        };

        Assert.Equal(27, Assert.IsType<MeasuredGpuSample>(GpuLoadCalculator.Calculate(Selected, items)).Percentage);
    }

    [Fact]
    public void Calculator_rejects_empty_engine_from_selected_luid()
    {
        var sample = Assert.IsType<GpuUnavailableSample>(GpuLoadCalculator.Calculate(Selected, [new(Name(""), 0, true)]));
        Assert.Equal(UnavailabilityReason.Uncorrelated, sample.Reason);
    }

    [Fact]
    public void Calculator_sums_processes_per_engine_index_before_taking_the_maximum()
    {
        GpuCounterItem[] items =
        [
            new(Name("3D", process: 1, index: 2), 60, true),
            new(Name("3D", process: 2, index: 2), 20, true),
            new(Name("3D", process: 1, index: 3), 70, true),
            new(Name("Copy", process: 1, index: 4), 40, true),
        ];

        Assert.Equal(80, Assert.IsType<MeasuredGpuSample>(GpuLoadCalculator.Calculate(Selected, items)).Percentage);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(100.1)]
    public void Calculator_marks_non_finite_or_out_of_range_values_invalid(double value)
    {
        var sample = Assert.IsType<GpuUnavailableSample>(GpuLoadCalculator.Calculate(Selected, [new(Name("3D"), value, true)]));
        Assert.Equal(UnavailabilityReason.Invalid, sample.Reason);
    }

    [Fact]
    public void Calculator_distinguishes_uncorrelated_physical_index_and_skipped_values()
    {
        Assert.Equal(UnavailabilityReason.Uncorrelated,
            Assert.IsType<GpuUnavailableSample>(GpuLoadCalculator.Calculate(Selected, [])).Reason);
        Assert.Equal(UnavailabilityReason.Uncorrelated,
            Assert.IsType<GpuUnavailableSample>(GpuLoadCalculator.Calculate(Selected, [new("bad", 0, true)])).Reason);
        Assert.Equal(UnavailabilityReason.Uncorrelated,
            Assert.IsType<GpuUnavailableSample>(GpuLoadCalculator.Calculate(Selected, [new(Name("3D", physical: 1), 0, true)])).Reason);
        Assert.Equal(UnavailabilityReason.NotYetSampled,
            Assert.IsType<SampleUnavailable>(GpuLoadCalculator.Calculate(Selected, [new(Name("3D"), 0, false)])).Reason);
        Assert.Equal(UnavailabilityReason.Uncorrelated,
            Assert.IsType<GpuUnavailableSample>(GpuLoadCalculator.Calculate(Selected, [new(Name("OtherEngine"), 0, true)])).Reason);
    }

    private static string Name(string engine, int process = 7, int physical = 0, int index = 2) =>
        $"pid_{process}_luid_0x{unchecked((uint)Selected.Luid.HighPart):x8}_0x{Selected.Luid.LowPart:x8}_phys_{physical}_eng_{index}_engtype_{engine}";
}

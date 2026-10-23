using FluentFlow.Controls;

namespace FluentFlow.Tests;

public class PlaybackTicksTests
{
    [Theory]
    [InlineData(3, 1)]
    [InlineData(9, 2)]
    [InlineData(276, 46)]
    public void Bars_fill_the_width_at_a_steady_pitch(double width, int expected)
        => Assert.Equal(expected, PlaybackTicks.TickCount(width));

    [Fact]
    public void A_sliver_still_gets_one_bar() => Assert.Equal(1, PlaybackTicks.TickCount(0.5));

    [Fact]
    public void The_swell_peaks_at_the_position_and_fades_symmetrically()
    {
        Assert.Equal(1, PlaybackTicks.Swell(0), 6);
        Assert.Equal(PlaybackTicks.Swell(1.2), PlaybackTicks.Swell(-1.2), 9);
        Assert.True(PlaybackTicks.Swell(0.5) > PlaybackTicks.Swell(1.5));
        Assert.Equal(0, PlaybackTicks.Swell(3));
    }
}

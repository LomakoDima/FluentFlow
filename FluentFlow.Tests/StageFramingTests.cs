using FluentFlow.Controls;

namespace FluentFlow.Tests;

// The settings stage films a stretch of the model taskbar; these keep the camera on the taskbar and the framing tidy.
public class StageFramingTests
{
    [Fact]
    public void A_taskbar_shorter_than_the_stage_is_centred()
        => Assert.Equal(100, StageFraming.Follow(stage: 600, span: 400, wanted: -50));

    [Fact]
    public void The_subject_is_centred_when_both_ends_are_far_away()
        => Assert.Equal(-500, StageFraming.Follow(stage: 600, span: 1600, wanted: -500));

    [Theory]
    [InlineData(200)]   // would show empty space before the start
    [InlineData(-30)]   // the start is nearly in view anyway
    public void The_start_is_pulled_in_rather_than_overshot_or_clipped(double wanted)
        => Assert.Equal(0, StageFraming.Follow(stage: 600, span: 1600, wanted: wanted));

    [Theory]
    [InlineData(-1200)] // would show empty space past the end
    [InlineData(-960)]  // the tray is nearly in view anyway
    public void The_end_is_pulled_in_rather_than_overshot_or_clipped(double wanted)
        => Assert.Equal(-1000, StageFraming.Follow(stage: 600, span: 1600, wanted: wanted));
}

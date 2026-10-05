using System.Windows;
using FluentFlow.Controls;

namespace FluentFlow.Tests;

public class FlyoutPlacementTests
{
    // Everything is in physical pixels, exactly as the popup code feeds it.
    private static readonly Size Flyout = new(320, 151);
    private const double Gap = 8, Edge = 8;

    private static FlyoutPlacementResult Place(Rect anchor, Rect work, double scale = 1)
        => FlyoutPlacement.Calculate(Scale(anchor, scale), new Size(Flyout.Width * scale, Flyout.Height * scale),
            Scale(work, scale), Gap * scale, Edge * scale);

    private static Rect Scale(Rect r, double s) => new(r.X * s, r.Y * s, r.Width * s, r.Height * s);

    private static Rect Body(FlyoutPlacementResult r, double scale = 1)
        => new(r.Location, new Size(Flyout.Width * scale, Flyout.Height * scale));

    [Fact]
    public void Centres_above_the_anchor_with_the_gap()
    {
        var result = Place(new Rect(1116, 607, 36, 36), new Rect(0, 48, 1920, 1032));
        Assert.Equal(FlyoutSide.Above, result.Side);
        Assert.Equal(1134 - 160, result.Location.X);
        Assert.Equal(607 - Gap - Flyout.Height, result.Location.Y);
    }

    [Fact]
    public void Goes_below_when_the_taskbar_leaves_no_room_above()
    {
        // Taskbar docked on top: work area starts at y=48 and the anchor sits just under it.
        var result = Place(new Rect(1116, 101, 36, 36), new Rect(0, 48, 1920, 1032));
        Assert.Equal(FlyoutSide.Below, result.Side);
        Assert.Equal(101 + 36 + Gap, result.Location.Y);
    }

    [Fact]
    public void Stays_above_a_taskbar_docked_at_the_bottom()
    {
        var work = new Rect(0, 0, 1920, 1032);
        var result = Place(new Rect(1800, 990, 36, 36), work);
        Assert.Equal(FlyoutSide.Above, result.Side);
        Assert.True(Body(result).Bottom <= work.Bottom);
    }

    [Fact]
    public void Never_overlaps_a_taskbar_docked_on_the_left_or_right()
    {
        var left = new Rect(64, 0, 1856, 1080);
        var atLeft = Place(new Rect(70, 500, 36, 36), left);
        Assert.True(Body(atLeft).Left >= left.Left + Edge);

        var right = new Rect(0, 0, 1856, 1080);
        var atRight = Place(new Rect(1840, 500, 36, 36), right);
        Assert.True(Body(atRight).Right <= right.Right - Edge);
    }

    [Fact]
    public void Clamps_to_the_screen_edge_with_a_margin()
    {
        var work = new Rect(0, 48, 1920, 1032);
        var result = Place(new Rect(1884, 607, 36, 36), work);
        Assert.Equal(1920 - Edge - Flyout.Width, result.Location.X);
    }

    [Fact]
    public void Works_on_a_secondary_monitor_with_negative_coordinates()
    {
        // A monitor to the left of and above the primary one.
        var work = new Rect(-1920, -200, 1920, 1040);
        var result = Place(new Rect(-30, -150, 36, 36), work);
        var body = Body(result);
        Assert.True(body.Left >= work.Left && body.Right <= work.Right);
        Assert.True(body.Top >= work.Top && body.Bottom <= work.Bottom);
        Assert.Equal(FlyoutSide.Below, result.Side);
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Scaling_everything_scales_the_result(double scale)
    {
        var anchor = new Rect(1116, 101, 36, 36);
        var work = new Rect(0, 48, 1920, 1032);
        var baseline = Place(anchor, work);
        var scaled = Place(anchor, work, scale);
        Assert.Equal(baseline.Side, scaled.Side);
        Assert.Equal(baseline.Location.X * scale, scaled.Location.X, 6);
        Assert.Equal(baseline.Location.Y * scale, scaled.Location.Y, 6);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Result_is_always_inside_the_work_area(double scale)
    {
        var work = Scale(new Rect(0, 48, 1920, 1032), scale);
        foreach (var x in new[] { 0.0, 600, 1884 })
        foreach (var y in new[] { 48.0, 300, 1040 })
        {
            var result = Place(new Rect(x, y, 36, 36), new Rect(0, 48, 1920, 1032), scale);
            var body = Body(result, scale);
            Assert.True(body.Left >= work.Left && body.Right <= work.Right, $"x at {x},{y} scale {scale}");
            Assert.True(body.Top >= work.Top && body.Bottom <= work.Bottom, $"y at {x},{y} scale {scale}");
        }
    }

    [Fact]
    public void Picks_the_roomier_side_when_neither_fits_and_still_stays_on_screen()
    {
        var work = new Rect(0, 0, 800, 200);
        var result = Place(new Rect(400, 120, 36, 36), work);
        Assert.Equal(FlyoutSide.Above, result.Side);
        Assert.True(Body(result).Top >= work.Top && Body(result).Bottom <= work.Bottom);
    }

    [Fact]
    public void A_flyout_wider_than_the_work_area_is_left_aligned()
    {
        var result = FlyoutPlacement.Calculate(new Rect(100, 300, 36, 36), new Size(500, 100), new Rect(0, 0, 400, 800), Gap, Edge);
        Assert.Equal(Edge, result.Location.X);
    }
}

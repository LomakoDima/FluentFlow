using System.Windows;
using FluentFlow.Controls;

namespace FluentFlow.Tests;

// A taskbar at the left or right edge puts the widget outside the work area sideways, so the flyout opens beside it.
public class FlyoutPlacementSideTests
{
    private static readonly Size Flyout = new(320, 151);
    private const double Gap = 8, Edge = 8;

    // Taskbar on the left: 62 px wide, widget in its middle; the work area starts at its edge.
    private static readonly Rect LeftWork = new(62, 0, 1858, 1080);
    private static readonly Rect LeftWidget = new(13, 768, 36, 128);
    // Taskbar on the right: the work area ends where the taskbar begins.
    private static readonly Rect RightWork = new(0, 0, 1858, 1080);
    private static readonly Rect RightWidget = new(1871, 768, 36, 128);

    private static FlyoutPlacementResult Place(Rect anchor, Rect work, double scale = 1)
        => FlyoutPlacement.Calculate(Scale(anchor, scale), new Size(Flyout.Width * scale, Flyout.Height * scale),
            Scale(work, scale), Gap * scale, Edge * scale);

    private static Rect Scale(Rect r, double s) => new(r.X * s, r.Y * s, r.Width * s, r.Height * s);

    [Fact]
    public void A_left_taskbar_opens_the_flyout_to_the_right_of_the_widget()
    {
        var result = Place(LeftWidget, LeftWork);
        Assert.Equal(FlyoutSide.Right, result.Side);
        Assert.Equal(62 + Edge, result.Location.X); // the widget is inside the taskbar, so the taskbar edge decides
        Assert.Equal(768 + (128 - Flyout.Height) / 2, result.Location.Y); // centred on the widget
    }

    [Fact]
    public void A_right_taskbar_opens_the_flyout_to_the_left_of_the_widget()
    {
        var result = Place(RightWidget, RightWork);
        Assert.Equal(FlyoutSide.Left, result.Side);
        Assert.Equal(1858 - Edge - Flyout.Width, result.Location.X);
        Assert.Equal(768 + (128 - Flyout.Height) / 2, result.Location.Y);
    }

    [Fact]
    public void A_gap_larger_than_the_edge_margin_is_kept_from_the_widget()
    {
        // The anchor is 13 px inside a 62 px taskbar; a 30 px gap from it lands beyond the 8 px edge margin.
        var result = FlyoutPlacement.Calculate(LeftWidget, Flyout, LeftWork, gap: 30, edgeMargin: Edge);
        Assert.Equal(13 + 36 + 30, result.Location.X);
    }

    [Theory]
    [InlineData(950, 1080 - Edge - 151)] // low on the screen: pushed up to stay inside the work area
    [InlineData(4, Edge)]                // high on the screen: pushed down
    public void The_flyout_stays_inside_the_work_area_vertically(double widgetTop, double expectedY)
    {
        var result = Place(new Rect(13, widgetTop, 36, 128), LeftWork);
        Assert.Equal(FlyoutSide.Right, result.Side);
        Assert.Equal(expectedY, result.Location.Y);
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Scaled_displays_scale_every_distance(double scale)
    {
        var result = Place(LeftWidget, LeftWork, scale);
        Assert.Equal(FlyoutSide.Right, result.Side);
        Assert.Equal((62 + Edge) * scale, result.Location.X, 6);
        Assert.Equal((768 + (128 - Flyout.Height) / 2) * scale, result.Location.Y, 6);
    }

    [Fact]
    public void A_secondary_monitor_with_negative_coordinates_works_too()
    {
        var work = new Rect(-2560 + 62, 0, 2560 - 62, 1080);
        var result = Place(new Rect(-2560 + 13, 768, 36, 128), work);
        Assert.Equal(FlyoutSide.Right, result.Side);
        Assert.Equal(-2560 + 62 + Edge, result.Location.X);
    }

    [Fact]
    public void An_anchor_inside_the_work_area_still_opens_above_or_below()
    {
        // The volume flyout is anchored to a button inside the player window, which is always inside the work area.
        var result = Place(new Rect(600, 600, 34, 34), LeftWork);
        Assert.Equal(FlyoutSide.Above, result.Side);
    }

    [Fact]
    public void A_work_area_narrower_than_the_flyout_never_throws_and_stays_on_screen()
    {
        var result = Place(LeftWidget, new Rect(62, 0, 200, 1080));
        Assert.Equal(FlyoutSide.Right, result.Side);
        Assert.Equal(62, result.Location.X);
    }
}

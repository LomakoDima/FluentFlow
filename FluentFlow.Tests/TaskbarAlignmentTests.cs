using System.Windows;
using FluentFlow.Controls;
using FluentFlow.Services;

namespace FluentFlow.Tests;

// The user-chosen alignment: Auto follows the Windows icon alignment, the others are taken literally.
public class TaskbarAlignmentTests
{
    private static readonly Rect Taskbar = new(0, 0, 1920, 48);
    private static readonly Rect Tray = new(1637, 0, 283, 48);
    private static readonly Size Wished = new(200, 36);
    private const double MinWidth = 124, Gap = 4, Pad = 2;

    private static Rect Icons(double left, double right) => new(left, 0, right - left, 48);

    private static Rect? Dock(WidgetAlignment alignment, bool centered, Rect[]? buttons = null, double offset = 0,
        Rect? tray = null, Rect? taskbar = null)
        => TaskbarDocking.Calculate(taskbar ?? Taskbar, tray ?? Tray, buttons ?? [], Wished, MinWidth, 1, Gap, Pad,
            alignment, centered, offset);

    [Theory]
    [InlineData(WidgetAlignment.Auto, true, true, WidgetAlignment.Left)]    // centred icons: free space is at the far edge
    [InlineData(WidgetAlignment.Auto, false, true, WidgetAlignment.Right)]  // left-aligned icons: free space by the tray
    [InlineData(WidgetAlignment.Auto, true, false, WidgetAlignment.Right)]  // right-to-left: tray on the left, far edge is the right
    [InlineData(WidgetAlignment.Auto, false, false, WidgetAlignment.Left)]
    [InlineData(WidgetAlignment.Left, false, true, WidgetAlignment.Left)]   // an explicit choice always wins
    [InlineData(WidgetAlignment.Center, true, true, WidgetAlignment.Center)]
    [InlineData(WidgetAlignment.Right, true, true, WidgetAlignment.Right)]
    public void Auto_follows_the_windows_icon_alignment_and_explicit_choices_win(
        WidgetAlignment chosen, bool iconsCentered, bool trayOnRight, WidgetAlignment expected)
        => Assert.Equal(expected, TaskbarDocking.Resolve(chosen, iconsCentered, trayOnRight));

    [Fact]
    public void Auto_with_centred_icons_goes_to_the_left_edge()
    {
        var icons = new[] { Icons(800, 1120) };
        var rect = Dock(WidgetAlignment.Auto, centered: true, icons)!.Value;
        Assert.Equal(Gap, rect.X);
        Assert.Equal(200, rect.Width);
    }

    [Fact]
    public void Auto_with_left_aligned_icons_sits_by_the_notification_area()
    {
        var icons = new[] { Icons(0, 500) };
        var rect = Dock(WidgetAlignment.Auto, centered: false, icons)!.Value;
        Assert.Equal(1637 - Gap, rect.Right);
    }

    [Fact]
    public void Left_goes_to_the_first_free_gap_even_when_icons_start_at_the_edge()
    {
        var icons = new[] { Icons(0, 500) };
        var rect = Dock(WidgetAlignment.Left, centered: false, icons)!.Value;
        Assert.Equal(500 + Gap, rect.X);
    }

    [Fact]
    public void Right_with_centred_icons_uses_the_gap_between_the_icons_and_the_tray()
    {
        var icons = new[] { Icons(800, 1120) };
        var rect = Dock(WidgetAlignment.Right, centered: true, icons)!.Value;
        Assert.Equal(1637 - Gap, rect.Right);
        Assert.True(rect.Left >= 1120 + Gap);
    }

    [Fact]
    public void Center_is_centred_on_the_taskbar_when_that_spot_is_free()
    {
        var icons = new[] { Icons(0, 500) };
        var rect = Dock(WidgetAlignment.Center, centered: false, icons)!.Value;
        Assert.Equal(960, rect.X + rect.Width / 2);
    }

    [Fact]
    public void Center_moves_to_the_nearest_free_gap_when_centred_icons_are_in_the_way()
    {
        var icons = new[] { Icons(800, 1120) };
        var rect = Dock(WidgetAlignment.Center, centered: true, icons)!.Value;
        Assert.True(rect.Right <= 800 - Gap || rect.Left >= 1120 + Gap);
    }

    [Fact]
    public void The_distance_from_the_edge_pushes_the_widget_inwards()
    {
        var left = Dock(WidgetAlignment.Left, centered: true, [Icons(800, 1120)], offset: 30)!.Value;
        Assert.Equal(Gap + 30, left.X);

        var right = Dock(WidgetAlignment.Right, centered: false, [], offset: 30)!.Value;
        Assert.Equal(1637 - Gap - 30, right.Right);
    }

    [Fact]
    public void A_gap_that_is_too_small_is_skipped_for_the_next_one()
    {
        // 60 px between two icons is no use; the widget takes the next gap that fits.
        var icons = new[] { Icons(0, 300), Icons(364, 700) };
        var rect = Dock(WidgetAlignment.Left, centered: false, icons)!.Value;
        Assert.True(rect.X >= 700 + Gap);
    }

    [Fact]
    public void A_gap_narrower_than_the_wished_width_shrinks_the_widget_down_to_the_minimum()
    {
        var icons = new[] { Icons(0, 1480) };
        var rect = Dock(WidgetAlignment.Right, centered: false, icons)!.Value;
        Assert.True(rect.Width < 200 && rect.Width >= MinWidth);
        Assert.Equal(1637 - Gap, rect.Right);
    }

    [Fact]
    public void Right_to_left_auto_with_centred_icons_goes_to_the_right_edge()
    {
        var tray = new Rect(0, 0, 283, 48);
        var rect = Dock(WidgetAlignment.Auto, centered: true, [Icons(800, 1120)], tray: tray)!.Value;
        Assert.Equal(1920 - Gap, rect.Right);
    }

    [Fact]
    public void Works_on_a_bottom_taskbar_and_a_secondary_monitor_for_every_alignment()
    {
        var taskbar = new Rect(-1920, 1032, 1920, 48);
        var tray = new Rect(-283, 1032, 283, 48);
        foreach (var alignment in Enum.GetValues<WidgetAlignment>())
        {
            var rect = Dock(alignment, centered: true, tray: tray, taskbar: taskbar)!.Value;
            Assert.True(rect.Left >= taskbar.Left && rect.Right <= taskbar.Right, alignment.ToString());
            Assert.True(rect.Top >= taskbar.Top && rect.Bottom <= taskbar.Bottom, alignment.ToString());
        }
    }

    [Fact]
    public void No_alignment_ever_overlaps_an_icon_or_the_tray()
    {
        var icons = new[] { Icons(0, 220), Icons(600, 1000), Icons(1200, 1300) };
        foreach (var alignment in Enum.GetValues<WidgetAlignment>())
        foreach (var centered in new[] { true, false })
        {
            var spot = Dock(alignment, centered, icons);
            if (spot is not { } rect) continue;
            foreach (var icon in icons) Assert.False(rect.IntersectsWith(icon), $"{alignment}/{centered} overlaps an icon");
            Assert.False(rect.IntersectsWith(Tray), $"{alignment}/{centered} overlaps the tray");
        }
    }
}

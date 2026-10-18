using System.Windows;
using FluentFlow.Controls;
using FluentFlow.Services;

namespace FluentFlow.Tests;

// A taskbar docked at the left or right edge runs top to bottom. The same rules apply along the other axis: the tray
// and every button are blocked out, "Left" means the top of the strip and "Right" the bottom. The nominal size is always
// (length along the taskbar, thickness across it), so the widget below is Size(128, 36) whichever way the taskbar runs.
public class TaskbarDockingVerticalTests
{
    private static readonly Size Nominal = new(128, 36);
    private const double MinLength = 72, Gap = 4, Pad = 2;

    // The classic Windows 10 shape: 62 px wide, the notification area in the bottom 180 px.
    private static readonly Rect LeftTaskbar = new(0, 0, 62, 1080);
    private static readonly Rect LeftTray = new(0, 900, 62, 180);
    private static readonly Rect RightTaskbar = new(1858, 0, 62, 1080);
    private static readonly Rect RightTray = new(1858, 900, 62, 180);

    private static Rect Column(double top, double bottom, Rect taskbar) => new(taskbar.X, top, taskbar.Width, bottom - top);

    private static Rect? Dock(Rect taskbar, Rect tray, IEnumerable<Rect>? buttons = null, double scale = 1,
        WidgetAlignment alignment = WidgetAlignment.Auto, bool iconsCentered = false, double offset = 0)
        => TaskbarDocking.Calculate(taskbar, tray, buttons ?? [], Nominal, MinLength, scale, Gap, Pad, alignment, iconsCentered, offset);

    [Fact]
    public void Is_vertical_only_when_the_taskbar_is_taller_than_wide()
    {
        Assert.True(TaskbarDocking.IsVertical(LeftTaskbar));
        Assert.False(TaskbarDocking.IsVertical(new Rect(0, 1032, 1920, 48)));
        Assert.False(TaskbarDocking.IsVertical(new Rect(0, 0, 100, 100)));
        Assert.False(TaskbarDocking.IsVertical(Rect.Empty));
    }

    [Fact]
    public void Left_taskbar_docks_above_the_tray_centred_across_the_taskbar()
    {
        var rect = Dock(LeftTaskbar, LeftTray)!.Value;
        Assert.Equal(new Rect(13, 900 - Gap - 128, 36, 128), rect);
    }

    [Fact]
    public void Right_taskbar_docks_above_the_tray_too()
    {
        var rect = Dock(RightTaskbar, RightTray)!.Value;
        Assert.Equal(new Rect(1858 + 13, 900 - Gap - 128, 36, 128), rect);
    }

    [Theory]
    [InlineData(1.25, 78)]
    [InlineData(1.5, 93)]
    [InlineData(2.0, 124)]
    public void Scaled_displays_scale_the_widget_with_the_taskbar(double scale, double taskbarWidth)
    {
        var taskbar = new Rect(0, 0, taskbarWidth, 1440);
        var tray = new Rect(0, 1100, taskbarWidth, 340);
        var rect = Dock(taskbar, tray, scale: scale)!.Value;
        Assert.Equal(Math.Round(36 * scale), rect.Width);
        Assert.Equal(Math.Round(128 * scale), rect.Height);
        Assert.Equal(Math.Round(1100 - Gap * scale), rect.Bottom);
        Assert.True(rect.Left >= 0 && rect.Right <= taskbarWidth);
    }

    [Fact]
    public void Secondary_monitor_with_negative_coordinates()
    {
        var taskbar = new Rect(-2560, 0, 62, 1080);
        var rect = Dock(taskbar, new Rect(-2560, 900, 62, 180))!.Value;
        Assert.Equal(-2560 + 13, rect.X);
        Assert.True(rect.Left >= -2560 && rect.Right <= -2560 + 62);
    }

    [Fact]
    public void A_narrow_taskbar_shrinks_the_widget_thickness_instead_of_failing()
    {
        // Small taskbar buttons: a 32 px wide taskbar leaves 28 px for the widget.
        var rect = Dock(new Rect(0, 0, 32, 1080), new Rect(0, 900, 32, 180))!.Value;
        Assert.Equal(28, rect.Width);
        Assert.Equal(2, rect.X);
    }

    [Fact]
    public void A_taskbar_too_thin_for_the_visualizer_is_unsupported()
    {
        Assert.Null(Dock(new Rect(0, 0, 26, 1080), new Rect(0, 900, 26, 180)));
    }

    [Fact]
    public void A_square_taskbar_has_no_direction_and_is_unsupported()
    {
        Assert.Null(Dock(new Rect(0, 0, 400, 400), new Rect(0, 300, 400, 100)));
    }

    [Fact]
    public void A_missing_or_misplaced_notification_area_is_unsupported()
    {
        Assert.Null(Dock(LeftTaskbar, Rect.Empty));
        Assert.Null(Dock(LeftTaskbar, new Rect(0, 900, 62, 0)));
        Assert.Null(Dock(LeftTaskbar, new Rect(500, 900, 62, 180))); // beside this taskbar, not on it
    }

    [Fact]
    public void Plenty_of_room_keeps_the_full_length()
    {
        var buttons = new[] { Column(0, 55, LeftTaskbar), Column(99, 495, LeftTaskbar) };
        Assert.Equal(128, Dock(LeftTaskbar, LeftTray, buttons)!.Value.Height);
    }

    [Fact]
    public void A_crowded_taskbar_shrinks_the_widget_to_stay_off_the_icons()
    {
        // The widget would start at 768, but the icons already reach 780: it starts after them and gets shorter.
        var rect = Dock(LeftTaskbar, LeftTray, [Column(0, 780, LeftTaskbar)])!.Value;
        Assert.Equal(780 + Gap, rect.Top);
        Assert.Equal(900 - Gap, rect.Bottom);
        Assert.True(rect.Height < 128 && rect.Height >= MinLength);
    }

    [Fact]
    public void A_full_taskbar_leaves_no_spot()
    {
        Assert.Null(Dock(LeftTaskbar, LeftTray, [Column(0, 850, LeftTaskbar)]));
    }

    [Fact]
    public void Tray_buttons_reported_by_automation_do_not_count_as_icons_above_it()
    {
        var buttons = new[] { Column(910, 940, LeftTaskbar), Column(960, 1060, LeftTaskbar) };
        Assert.Equal(128, Dock(LeftTaskbar, LeftTray, buttons)!.Value.Height);
    }

    [Fact]
    public void A_tray_at_the_top_puts_the_widget_below_it()
    {
        var rect = Dock(LeftTaskbar, new Rect(0, 0, 62, 180))!.Value;
        Assert.Equal(180 + Gap, rect.Top);
    }

    [Theory]
    [InlineData(WidgetAlignment.Left, 4)]        // the top of the strip
    [InlineData(WidgetAlignment.Center, 476)]    // centred on the taskbar: 540 - 64
    [InlineData(WidgetAlignment.Right, 768)]     // the bottom, against the tray
    public void Alignment_maps_to_top_centre_and_bottom(WidgetAlignment alignment, double expectedTop)
        => Assert.Equal(expectedTop, Dock(LeftTaskbar, LeftTray, alignment: alignment)!.Value.Top);

    [Fact]
    public void Auto_with_centred_icons_goes_to_the_top_edge()
        => Assert.Equal(Gap, Dock(LeftTaskbar, LeftTray, alignment: WidgetAlignment.Auto, iconsCentered: true)!.Value.Top);

    [Fact]
    public void Auto_with_icons_at_the_top_sits_by_the_notification_area()
    {
        var rect = Dock(LeftTaskbar, LeftTray, [Column(0, 300, LeftTaskbar)], iconsCentered: false)!.Value;
        Assert.Equal(900 - Gap, rect.Bottom);
    }

    [Fact]
    public void Top_never_overlaps_icons_that_start_at_the_top_edge()
    {
        var rect = Dock(LeftTaskbar, LeftTray, [Column(0, 300, LeftTaskbar)], alignment: WidgetAlignment.Left)!.Value;
        Assert.Equal(300 + Gap, rect.Top);
    }

    [Fact]
    public void Distance_from_the_edge_moves_the_widget_along_the_taskbar()
    {
        Assert.Equal(Gap + 20, Dock(LeftTaskbar, LeftTray, alignment: WidgetAlignment.Left, offset: 20)!.Value.Top);
        Assert.Equal(900 - Gap - 20, Dock(LeftTaskbar, LeftTray, alignment: WidgetAlignment.Right, offset: 20)!.Value.Bottom);
    }
}

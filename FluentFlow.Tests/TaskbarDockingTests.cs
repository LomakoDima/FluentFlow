using System.Windows;
using FluentFlow.Controls;

namespace FluentFlow.Tests;

// Each case is a taskbar layout that is not the author's machine: another edge, DPI, monitor, language, crowding.
public class TaskbarDockingTests
{
    private static readonly Size Nominal = new(128, 36);
    private const double MinWidth = 72, Gap = 4, Pad = 2;

    private static Rect? Dock(Rect taskbar, Rect tray, IEnumerable<Rect>? buttons = null, double scale = 1)
        => TaskbarDocking.Calculate(taskbar, tray, buttons ?? [], Nominal, MinWidth, scale, Gap, Pad);

    private static Rect Buttons(double left, double right, double top, double height) => new(left, top, right - left, height);

    [Fact]
    public void Top_taskbar_like_the_authors_machine()
    {
        var rect = Dock(new Rect(0, 0, 1920, 48), new Rect(1637, 0, 283, 48))!.Value;
        Assert.Equal(new Rect(1505, 6, 128, 36), rect);
    }

    [Fact]
    public void Bottom_taskbar_the_normal_windows_layout()
    {
        var rect = Dock(new Rect(0, 1032, 1920, 48), new Rect(1637, 1032, 283, 48))!.Value;
        Assert.Equal(new Rect(1505, 1038, 128, 36), rect);
    }

    [Theory]
    [InlineData(1.25, 60)]
    [InlineData(1.5, 72)]
    [InlineData(2.0, 96)]
    public void Scaled_displays_scale_the_widget_with_the_taskbar(double scale, double taskbarHeight)
    {
        var taskbar = new Rect(0, 0, 1920, taskbarHeight);
        var tray = new Rect(1500, 0, 420, taskbarHeight);
        var rect = Dock(taskbar, tray, scale: scale)!.Value;
        Assert.Equal(Math.Round(128 * scale), rect.Width);
        Assert.Equal(Math.Round(36 * scale), rect.Height);
        Assert.Equal(Math.Round(1500 - Gap * scale - 128 * scale), rect.X);
        Assert.True(rect.Top >= 0 && rect.Bottom <= taskbarHeight);
    }

    [Fact]
    public void Secondary_monitor_with_negative_coordinates()
    {
        var rect = Dock(new Rect(-1920, 1032, 1920, 48), new Rect(-283, 1032, 283, 48))!.Value;
        Assert.Equal(-283 - Gap - 128, rect.X);
        Assert.True(rect.Left >= -1920 && rect.Right <= 0);
    }

    [Fact]
    public void Small_taskbar_buttons_shrink_the_widget_height_instead_of_failing()
    {
        // "Use small taskbar buttons": a 32 px taskbar.
        var rect = Dock(new Rect(0, 0, 1920, 32), new Rect(1637, 0, 283, 32))!.Value;
        Assert.Equal(28, rect.Height);
        Assert.Equal(2, rect.Top);
    }

    [Fact]
    public void A_taskbar_too_thin_for_the_visualizer_is_unsupported()
    {
        Assert.Null(Dock(new Rect(0, 0, 1920, 26), new Rect(1637, 0, 283, 26)));
    }

    [Fact]
    public void Vertical_taskbars_are_unsupported_so_the_app_falls_back_to_a_window()
    {
        Assert.Null(Dock(new Rect(0, 0, 62, 1080), new Rect(0, 900, 62, 180)));
    }

    [Fact]
    public void A_missing_or_empty_notification_area_is_unsupported()
    {
        Assert.Null(Dock(new Rect(0, 0, 1920, 48), Rect.Empty));
        Assert.Null(Dock(new Rect(0, 0, 1920, 48), new Rect(1900, 0, 0, 48)));
        Assert.Null(Dock(new Rect(0, 0, 1920, 48), new Rect(1637, 600, 283, 48))); // not on this taskbar
    }

    [Fact]
    public void Plenty_of_room_keeps_the_full_size()
    {
        var buttons = new[] { Buttons(0, 55, 0, 48), Buttons(99, 495, 0, 48) };
        var rect = Dock(new Rect(0, 0, 1920, 48), new Rect(1637, 0, 283, 48), buttons)!.Value;
        Assert.Equal(128, rect.Width);
    }

    [Fact]
    public void A_crowded_taskbar_shrinks_the_widget_to_stay_off_the_icons()
    {
        // The widget would start at 1505, but the icons already reach 1550: it has to start after them and shrink.
        var buttons = new[] { Buttons(0, 1550, 0, 48) };
        var rect = Dock(new Rect(0, 0, 1920, 48), new Rect(1637, 0, 283, 48), buttons)!.Value;
        Assert.True(rect.Left >= 1550 + Gap);
        Assert.Equal(1637 - Gap, rect.Right);
        Assert.True(rect.Width < 128 && rect.Width >= MinWidth);
    }

    [Fact]
    public void A_full_taskbar_leaves_no_spot()
    {
        var buttons = new[] { Buttons(0, 1620, 0, 48) };
        Assert.Null(Dock(new Rect(0, 0, 1920, 48), new Rect(1637, 0, 283, 48), buttons));
    }

    [Fact]
    public void Tray_buttons_reported_by_automation_do_not_count_as_icons_in_front_of_it()
    {
        // Win10/11 automation also lists buttons inside the notification area; those lie within the tray rectangle.
        var tray = new Rect(1637, 0, 283, 48);
        var buttons = new[] { Buttons(1640, 1670, 0, 48), Buttons(1700, 1900, 0, 48) };
        var rect = Dock(new Rect(0, 0, 1920, 48), tray, buttons)!.Value;
        Assert.Equal(128, rect.Width);
    }

    [Fact]
    public void Right_to_left_windows_puts_the_tray_on_the_left_and_docks_to_its_right()
    {
        var rect = Dock(new Rect(0, 0, 1920, 48), new Rect(0, 0, 283, 48))!.Value;
        Assert.Equal(283 + Gap, rect.X);
        Assert.Equal(128, rect.Width);
    }

    [Fact]
    public void Right_to_left_crowded_taskbar_also_shrinks()
    {
        var buttons = new[] { Buttons(420, 1920, 0, 48) };
        var rect = Dock(new Rect(0, 0, 1920, 48), new Rect(0, 0, 283, 48), buttons)!.Value;
        Assert.True(rect.Right <= 420 - Gap);
        Assert.True(rect.Width >= MinWidth);
    }
}

using System.Windows;

namespace FluentFlow.Controls;

// Pure geometry for the taskbar widget, in physical pixels: where the widget fits next to the notification
// area, given the taskbar, the tray and the taskbar's buttons. No windows involved, so every taskbar layout
// (top/bottom, any DPI, any monitor, either text direction, crowded) can be checked without owning that machine.
internal static class TaskbarDocking
{
    // Below this height the visualizer would be clipped, so such a taskbar is treated as unsupported.
    private const double MinimumHeight = 24; // DIPs

    // Returns the widget's rectangle in screen pixels, or null when there is no usable spot (vertical taskbar,
    // unknown tray, no room left between the buttons and the tray).
    public static Rect? Calculate(Rect taskbar, Rect notificationArea, IEnumerable<Rect> taskbarButtons,
        Size nominalSize, double minimumWidth, double scale, double gap, double verticalPadding)
    {
        if (taskbar.IsEmpty || taskbar.Width <= taskbar.Height) return null; // vertical taskbars are not supported
        if (notificationArea.IsEmpty || notificationArea.Width <= 0
            || notificationArea.Bottom <= taskbar.Top || notificationArea.Top >= taskbar.Bottom) return null;

        var gapPx = gap * scale;
        var height = Math.Min(nominalSize.Height * scale, taskbar.Height - 2 * verticalPadding * scale);
        if (height < MinimumHeight * scale) return null;

        var nominalWidth = nominalSize.Width * scale;
        var buttons = taskbarButtons.Where(b => !b.IsEmpty && b.Width > 0 && !b.IntersectsWith(notificationArea)).ToList();
        double left, right;
        if (notificationArea.Left + notificationArea.Width / 2 >= taskbar.Left + taskbar.Width / 2)
        {
            // Tray on the right (left-to-right Windows): dock to its left, never over the buttons before it.
            right = notificationArea.Left - gapPx;
            left = right - nominalWidth;
            var bound = taskbar.Left;
            var before = buttons.Where(b => b.Right <= notificationArea.Left + 1).Select(b => b.Right).ToList();
            if (before.Count > 0) bound = Math.Max(bound, before.Max() + gapPx);
            left = Math.Max(left, bound);
        }
        else
        {
            // Tray on the left (right-to-left Windows): dock to its right.
            left = notificationArea.Right + gapPx;
            right = left + nominalWidth;
            var bound = taskbar.Right;
            var after = buttons.Where(b => b.Left >= notificationArea.Right - 1).Select(b => b.Left).ToList();
            if (after.Count > 0) bound = Math.Min(bound, after.Min() - gapPx);
            right = Math.Min(right, bound);
        }

        var width = right - left;
        if (width < minimumWidth * scale) return null;
        var top = taskbar.Top + (taskbar.Height - height) / 2;
        return new Rect(Math.Round(left), Math.Round(top), Math.Round(width), Math.Round(height));
    }
}

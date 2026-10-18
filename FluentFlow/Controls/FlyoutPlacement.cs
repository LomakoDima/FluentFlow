using System.Windows;

namespace FluentFlow.Controls;

// Where the flyout sits relative to its anchor. Left and Right are for a taskbar docked at a screen edge: the flyout
// is beside the widget, on the side away from the taskbar.
internal enum FlyoutSide { Above, Below, Left, Right }

internal readonly record struct FlyoutPlacementResult(Point Location, FlyoutSide Side);

// Pure geometry in physical pixels, so it can be checked for any monitor layout and DPI without a window.
internal static class FlyoutPlacement
{
    // Centres the flyout on the anchor, always inside the work area (the monitor minus the taskbar, wherever the
    // taskbar is docked). An anchor on a taskbar at the left or right edge lies outside the work area sideways, so the
    // flyout opens beside it; otherwise it goes above the anchor when it fits and below it when it does not.
    public static FlyoutPlacementResult Calculate(Rect anchor, Size flyout, Rect workArea, double gap, double edgeMargin)
    {
        if (anchor.Right <= workArea.Left) return Beside(anchor, flyout, workArea, gap, edgeMargin, FlyoutSide.Right);
        if (anchor.Left >= workArea.Right) return Beside(anchor, flyout, workArea, gap, edgeMargin, FlyoutSide.Left);

        var minX = workArea.Left + edgeMargin;
        var maxX = workArea.Right - edgeMargin - flyout.Width;
        var x = anchor.Left + (anchor.Width - flyout.Width) / 2;
        x = maxX < minX ? minX : Math.Clamp(x, minX, maxX);

        var above = anchor.Top - gap - flyout.Height;
        var below = anchor.Bottom + gap;
        FlyoutSide side;
        if (above >= workArea.Top) side = FlyoutSide.Above;
        else if (below + flyout.Height <= workArea.Bottom) side = FlyoutSide.Below;
        else side = anchor.Top - workArea.Top >= workArea.Bottom - anchor.Bottom ? FlyoutSide.Above : FlyoutSide.Below;

        var y = side == FlyoutSide.Above ? above : below;
        var maxY = workArea.Bottom - flyout.Height;
        y = maxY < workArea.Top ? workArea.Top : Math.Clamp(y, workArea.Top, maxY);
        return new FlyoutPlacementResult(new Point(x, y), side);
    }

    // Vertically centred on the anchor, a gap away from it and never closer than the edge margin to the taskbar.
    private static FlyoutPlacementResult Beside(Rect anchor, Size flyout, Rect workArea, double gap, double edgeMargin, FlyoutSide side)
    {
        var x = side == FlyoutSide.Right
            ? Math.Max(anchor.Right + gap, workArea.Left + edgeMargin)
            : Math.Min(anchor.Left - gap - flyout.Width, workArea.Right - edgeMargin - flyout.Width);
        var minX = workArea.Left;
        var maxX = workArea.Right - flyout.Width;
        x = maxX < minX ? minX : Math.Clamp(x, minX, maxX);

        var minY = workArea.Top + edgeMargin;
        var maxY = workArea.Bottom - edgeMargin - flyout.Height;
        var y = anchor.Top + (anchor.Height - flyout.Height) / 2;
        y = maxY < minY ? minY : Math.Clamp(y, minY, maxY);
        return new FlyoutPlacementResult(new Point(x, y), side);
    }
}

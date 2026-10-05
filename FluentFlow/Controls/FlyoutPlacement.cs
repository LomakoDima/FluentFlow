using System.Windows;

namespace FluentFlow.Controls;

internal enum FlyoutSide { Above, Below }

internal readonly record struct FlyoutPlacementResult(Point Location, FlyoutSide Side);

// Pure geometry in physical pixels, so it can be checked for any monitor layout and DPI without a window.
internal static class FlyoutPlacement
{
    // Centres the flyout on the anchor, above it when it fits and below otherwise, always inside the work area
    // (the monitor minus the taskbar, wherever the taskbar is docked).
    public static FlyoutPlacementResult Calculate(Rect anchor, Size flyout, Rect workArea, double gap, double edgeMargin)
    {
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
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FluentFlow.Controls;

// A real WPF Popup (own HWND, tool window: no Alt+Tab entry, no taskbar button) anchored to a control.
// WPF's StaysOpen=false can't animate the close and ignores the taskbar, so dismissal and placement are done here.
[ContentProperty(nameof(Body))]
public sealed class FlyoutPopup : Popup
{
    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(UIElement), typeof(FlyoutPopup),
        new PropertyMetadata(null, (sender, args) => ((FlyoutPopup)sender)._frame.Child = (UIElement?)args.NewValue));

    // Transparent room around the body for its drop shadow; also the most the slide animation may travel.
    private const double ShadowMargin = 20;
    private const double SlideDistance = 10;
    private const double GapFromAnchor = 8;
    private const double ScreenEdgeMargin = 8;
    private const int WmActivate = 0x0006;

    private readonly Border _frame = new() { Padding = new Thickness(ShadowMargin) };
    private readonly TranslateTransform _slide = new();
    private Window? _owner;
    private IntPtr _ownerHandle;
    private IntPtr _popupHandle;
    private HwndSource? _popupSource;
    private FlyoutSide _side = FlyoutSide.Above;
    private bool _closing;
    private bool _restoreFocus;
    private bool _openedByKeyboard;

    public FlyoutPopup()
    {
        AllowsTransparency = true;
        StaysOpen = true;
        Placement = PlacementMode.Custom;
        CustomPopupPlacementCallback = PlaceFlyout;
        _frame.RenderTransform = _slide;
        _frame.KeyDown += Frame_KeyDown; // bubbling: the content may use Esc first (e.g. leave a sub-page)
        Child = _frame;
    }

    public UIElement? Body
    {
        get => (UIElement?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public void Toggle()
    {
        if (IsOpen && !_closing) Hide(restoreFocus: true);
        else Show();
    }

    public void Show()
    {
        if (PlacementTarget is not FrameworkElement anchor || Window.GetWindow(anchor) is not { } owner) return;
        if (IsOpen && !_closing) return;

        _openedByKeyboard = InputManager.Current.MostRecentInputDevice is KeyboardDevice;
        _restoreFocus = false;
        if (IsOpen)
        {
            // Re-opened while the close animation was still running: fade back in from where it is.
            _closing = false;
            PlayOpenAnimation();
            return;
        }

        AttachOwner(owner);
        ResetAnimations();
        // Invisible until Opened, so the first composed frame never shows the flyout at its final spot.
        _frame.Opacity = 0;
        IsOpen = true;
    }

    // restoreFocus: give keyboard focus back to the anchor. Not wanted when the user clicked another window.
    public void Hide(bool restoreFocus)
    {
        if (!IsOpen || _closing) return;
        _restoreFocus = restoreFocus;
        if (!SystemParameters.ClientAreaAnimation)
        {
            IsOpen = false;
            return;
        }

        _closing = true;
        var toward = _side == FlyoutSide.Above ? SlideDistance / 2 : -SlideDistance / 2;
        _slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(toward, TimeSpan.FromMilliseconds(100)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
        fade.Completed += (_, _) =>
        {
            if (!_closing) return;
            _closing = false;
            IsOpen = false;
        };
        _frame.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _popupSource = PresentationSource.FromVisual(_frame) as HwndSource;
        if (_popupSource is not null)
        {
            _popupHandle = _popupSource.Handle;
            _popupSource.AddHook(PopupWndProc);
        }
        PlayOpenAnimation();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusBody);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_popupSource is not null)
        {
            _popupSource.RemoveHook(PopupWndProc);
            _popupSource = null;
        }
        _popupHandle = IntPtr.Zero;
        _closing = false;

        var owner = _owner;
        DetachOwner();
        if (_restoreFocus && owner is not null && !owner.Dispatcher.HasShutdownStarted)
        {
            if (!owner.IsActive) owner.Activate();
            if (PlacementTarget is IInputElement anchor) Keyboard.Focus(anchor);
        }
        _restoreFocus = false;
    }

    private void PlayOpenAnimation()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            ResetAnimations();
            _frame.Opacity = 1;
            return;
        }

        var from = _side == FlyoutSide.Above ? SlideDistance : -SlideDistance;
        _slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _frame.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    private void ResetAnimations()
    {
        _slide.BeginAnimation(TranslateTransform.YProperty, null);
        _frame.BeginAnimation(UIElement.OpacityProperty, null);
        _slide.Y = 0;
    }

    private void FocusBody()
    {
        if (!IsOpen || Body is not { } body) return;
        if (_openedByKeyboard) body.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        else Keyboard.Focus(body); // Esc works without painting a focus ring on a mouse-opened flyout.
    }

    // Everything below decides when the flyout goes away.

    private void AttachOwner(Window owner)
    {
        DetachOwner();
        _owner = owner;
        _ownerHandle = new WindowInteropHelper(owner).Handle;
        owner.Deactivated += Owner_Deactivated;
        owner.LocationChanged += Owner_Moved;
        owner.SizeChanged += Owner_Moved;
        owner.StateChanged += Owner_Moved;
        owner.Closed += Owner_Moved;
        owner.IsVisibleChanged += Owner_VisibleChanged;
        owner.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(Owner_PreviewMouseDown), handledEventsToo: true);
        owner.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(Owner_PreviewKeyDown), handledEventsToo: true);
    }

    private void DetachOwner()
    {
        if (_owner is not { } owner) return;
        owner.Deactivated -= Owner_Deactivated;
        owner.LocationChanged -= Owner_Moved;
        owner.SizeChanged -= Owner_Moved;
        owner.StateChanged -= Owner_Moved;
        owner.Closed -= Owner_Moved;
        owner.IsVisibleChanged -= Owner_VisibleChanged;
        owner.RemoveHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(Owner_PreviewMouseDown));
        owner.RemoveHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(Owner_PreviewKeyDown));
        _owner = null;
        _ownerHandle = IntPtr.Zero;
    }

    // A click anywhere in the owner window other than the anchor dismisses the flyout. The anchor's own
    // Click toggles it, so pressing the anchor must not close it here first (that would reopen it on release).
    // The popup is a logical child of the owner, so clicks inside the flyout are routed here too: ignore them.
    private void Owner_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (IsWithin(source, _frame)) return;
        if (PlacementTarget is DependencyObject anchor && IsWithin(source, anchor)) return;
        Hide(restoreFocus: false);
    }

    // Keys typed inside the flyout reach the owner too (logical tree); those are Frame_KeyDown's business,
    // so the content gets the first chance to use Esc.
    private void Owner_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !IsWithin(e.OriginalSource as DependencyObject, _frame)) Hide(restoreFocus: true);
    }

    private void Frame_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled) return;
        e.Handled = true;
        Hide(restoreFocus: true);
    }

    private void Owner_Moved(object? sender, EventArgs e) => HideImmediately();

    private void Owner_VisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue) HideImmediately();
    }

    private void HideImmediately()
    {
        _closing = false;
        IsOpen = false;
    }

    // Activation moves between the owner and the popup window when the user clicks inside the flyout.
    // Only when some other window becomes foreground (another app, the taskbar, the desktop) do we close.
    private void Owner_Deactivated(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(DispatcherPriority.Input, CloseIfForegroundIsForeign);

    private IntPtr PopupWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmActivate && (wParam.ToInt64() & 0xFFFF) == 0)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, CloseIfForegroundIsForeign);
        return IntPtr.Zero;
    }

    private void CloseIfForegroundIsForeign()
    {
        if (!IsOpen) return;
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == _ownerHandle || foreground == _popupHandle) return;
        Hide(restoreFocus: false);
    }

    private static bool IsWithin(DependencyObject? element, DependencyObject ancestor)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, ancestor)) return true;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    // Placement. WPF hands this callback the popup and anchor sizes in device-independent units and expects
    // an offset from the anchor's top-left in the same units; the actual maths is done in physical pixels.

    private CustomPopupPlacement[] PlaceFlyout(Size popupSize, Size targetSize, Point offset)
    {
        if (PlacementTarget is not FrameworkElement anchor || PresentationSource.FromVisual(anchor) is null)
            return [new CustomPopupPlacement(new Point(0, 0), PopupPrimaryAxis.None)];

        var dpi = VisualTreeHelper.GetDpi(anchor);
        var anchorOrigin = anchor.PointToScreen(new Point(0, 0)); // physical pixels
        var anchorRect = new Rect(anchorOrigin,
            new Size(anchor.ActualWidth * dpi.DpiScaleX, anchor.ActualHeight * dpi.DpiScaleY));
        var visibleSize = new Size(
            Math.Max(0, popupSize.Width - 2 * ShadowMargin) * dpi.DpiScaleX,
            Math.Max(0, popupSize.Height - 2 * ShadowMargin) * dpi.DpiScaleY);

        var placement = FlyoutPlacement.Calculate(anchorRect, visibleSize, MonitorWorkArea.Get(anchorRect),
            GapFromAnchor * dpi.DpiScaleY, ScreenEdgeMargin * dpi.DpiScaleX);
        _side = placement.Side;

        // The popup window includes the shadow margin; the visible body is what gets aligned.
        var originX = placement.Location.X - ShadowMargin * dpi.DpiScaleX;
        var originY = placement.Location.Y - ShadowMargin * dpi.DpiScaleY;
        return [new CustomPopupPlacement(
            new Point((originX - anchorRect.X) / dpi.DpiScaleX, (originY - anchorRect.Y) / dpi.DpiScaleY),
            PopupPrimaryAxis.None)];
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    }
}

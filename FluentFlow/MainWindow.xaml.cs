using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FluentFlow.Controls;
using FluentFlow.Models;
using FluentFlow.Services;

namespace FluentFlow;

public partial class MainWindow : Window
{
    private const double GapFromWidget = 8; // DIPs
    private const double ShadowMargin = 24; // DIPs of transparent window around the card, for its shadow (see the XAML)
    private const double SlideDistance = 12; // DIPs the flyout travels while it opens
    private const double HideTravel = 0.5;   // a closing flyout drifts half as far as an opening one
    private const int ArtworkDissolve = 420; // ms; a cover change is slower than a control's motion, like a cut in a film
    private const int AmbientDissolve = 640;
    private static readonly TimeSpan MissingArtworkGrace = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan SkipMemory = TimeSpan.FromSeconds(4);
    private bool _widgetMode;
    private bool _exiting;

    // Where the flyout rests and how it moves in and out. Everything is in physical pixels, as SetWindowPos wants.
    private Point _rest;
    private Vector _towardTaskbar;
    private double _slide;
    private double _offset;          // 0: at rest, 1: fully displaced towards the taskbar
    private double _fromOffset, _toOffset, _fromOpacity;
    private int _motionMilliseconds;
    private IEasingFunction _motionEasing = Motion.Decelerate;
    private readonly Stopwatch _motionClock = new();
    private Action? _motionDone;
    private bool _moving;
    private bool _hiding;
    private bool _anchored;

    private MediaInfo? _lastMedia;

    // The cover on screen, which lags the media a little: a new track first reports no cover and then its own, and
    // flashing the placeholder in between would look broken. The placeholder only shows if no cover follows.
    private readonly ImageSource _fallbackArtwork;
    private readonly DispatcherTimer _missingArtworkTimer;
    private ImageSource? _shownArtwork;
    private string? _shownArtworkTitle;

    // Colours from the cover. The accent is inherited by everything in the window (PlayerAccent); the glow is the card's
    // wash.
    private readonly SolidColorBrush _accent = new(ArtworkPalette.Default.Accent);
    private readonly GradientStop _glowCenter = new(ArtworkPalette.Default.Glow, 0);
    private readonly GradientStop _glowMiddle = new(ArtworkPalette.Default.GlowSecondary, 0.6);
    private readonly GradientStop _glowEdge = new(Colors.Transparent, 1);

    // The last skip button pressed, so the new title can come in from the side it was asked for.
    private int _skipDirection = 1;
    private readonly Stopwatch _sinceSkip = new();

    public MainWindow(MediaSessionService media, SystemVolumeService volume)
    {
        InitializeComponent();
        PlayerAccent.SetBrush(this, _accent);
        Glow.Background = new RadialGradientBrush
        {
            Center = new Point(0.18, 0.3), GradientOrigin = new Point(0.18, 0.3), RadiusX = 0.95, RadiusY = 1.5,
            GradientStops = { _glowCenter, _glowMiddle, _glowEdge }
        };
        _fallbackArtwork = (ImageSource)FindResource("FallbackArtwork");
        _missingArtworkTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = MissingArtworkGrace };
        _missingArtworkTimer.Tick += (_, _) =>
        {
            _missingArtworkTimer.Stop();
            ShowArtwork(_fallbackArtwork, IsVisible);
        };

        DataContext = media;
        VolumePanel.DataContext = volume;
        VolumeFlyoutPopup.PlacementTarget = VolumeControl;
        media.PropertyChanged += Media_PropertyChanged;
        IsVisibleChanged += (_, _) => { if (IsVisible) PlayEntrance(); };
        ShowArtwork(media.CurrentMedia.Artwork ?? _fallbackArtwork, animate: false);
        ShowPlayState(media.CurrentMedia.IsPlaying, animate: false);
        ShowProgress(media.CurrentMedia, null);
    }

    // With a taskbar widget the window is a flyout: no taskbar button, closing only hides it, and it opens at the widget.
    public void UseWidgetMode()
    {
        _widgetMode = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        MinimizeButton.Visibility = Visibility.Collapsed;
    }

    // No usable taskbar widget: an ordinary window with a taskbar button, where closing quits the app.
    public void UsePlainMode()
    {
        _widgetMode = false;
        ShowInTaskbar = true;
        MinimizeButton.Visibility = Visibility.Visible;
    }

    public event EventHandler? SettingsRequested;

    public void CloseForExit()
    {
        _exiting = true;
        StopMotion();
        Close();
    }

    public void ToggleNear(Rect widgetPixels, double scale)
    {
        if (IsVisible && !_hiding)
        {
            HideFlyout();
            return;
        }

        // Opened again while it was still closing: it turns around from where it is.
        if (!IsVisible)
        {
            PlaceNear(widgetPixels, scale);
            _offset = 1;
            ApplyMotion(1, 0);
            Show();
        }
        _hiding = false;
        Activate();
        BeginMotion(0, Motion.Normal, Motion.Decelerate, null);
    }

    // Opens above or below the widget, wherever the taskbar leaves room, or beside it when the taskbar is at the left
    // or right edge of the screen; always inside the monitor's work area.
    private void PlaceNear(Rect widgetPixels, double scale)
    {
        if (widgetPixels.IsEmpty)
        {
            _anchored = false; // nowhere to anchor: it just fades in where it is
            return;
        }
        // The card is placed, not the window: the shadow margin around it may hang over the taskbar or the screen edge.
        var margin = ShadowMargin * scale;
        var size = new Size(Width * scale - 2 * margin, Height * scale - 2 * margin);
        var placement = FlyoutPlacement.Calculate(widgetPixels, size, MonitorWorkArea.Get(widgetPixels),
            GapFromWidget * scale, GapFromWidget * scale);
        _anchored = true;
        _rest = placement.Location - new Vector(margin, margin);
        _slide = SlideDistance * scale;
        // It grows away from the taskbar, so it starts out displaced towards it.
        _towardTaskbar = placement.Side switch
        {
            FlyoutSide.Above => new Vector(0, 1),
            FlyoutSide.Below => new Vector(0, -1),
            FlyoutSide.Right => new Vector(-1, 0),
            _ => new Vector(1, 0)
        };
    }

    private void HideFlyout()
    {
        if (!IsVisible || _hiding) return;
        VolumeFlyoutPopup.Hide(restoreFocus: false);
        if (!Motion.Enabled)
        {
            Hide();
            return;
        }
        _hiding = true;
        BeginMotion(HideTravel, Motion.Quick, Motion.Accelerate, () =>
        {
            _hiding = false;
            Hide();
            ApplyMotion(0, 1); // ready for the next time, with nothing left of the animation
        });
    }

    // One tween drives both the position and the opacity. Opening is solid a little before it settles; closing fades
    // out as it drifts. It runs from the current state, so reversing a running animation is seamless.
    private void BeginMotion(double toOffset, int milliseconds, IEasingFunction easing, Action? done)
    {
        if (!Motion.Enabled)
        {
            ApplyMotion(toOffset > 0 ? toOffset : 0, toOffset > 0 ? 0 : 1);
            done?.Invoke();
            return;
        }
        _fromOffset = _offset;
        _toOffset = toOffset;
        _fromOpacity = Opacity;
        _motionMilliseconds = milliseconds;
        _motionEasing = easing;
        _motionDone = done;
        _motionClock.Restart();
        if (_moving) return;
        _moving = true;
        CompositionTarget.Rendering += OnMotionFrame;
    }

    private void OnMotionFrame(object? sender, EventArgs e)
    {
        var progress = Math.Min(1, _motionClock.Elapsed.TotalMilliseconds / _motionMilliseconds);
        var eased = _motionEasing.Ease(progress);
        var offset = _fromOffset + (_toOffset - _fromOffset) * eased;
        var opening = _toOffset < _fromOffset;
        var opacity = opening
            ? _fromOpacity + (1 - _fromOpacity) * _motionEasing.Ease(Math.Min(1, progress * 1.3))
            : _fromOpacity * (1 - eased);
        ApplyMotion(offset, opacity);
        if (progress < 1) return;

        StopMotion();
        var done = _motionDone;
        _motionDone = null;
        done?.Invoke();
    }

    private void StopMotion()
    {
        if (!_moving) return;
        _moving = false;
        CompositionTarget.Rendering -= OnMotionFrame;
    }

    private void ApplyMotion(double offset, double opacity)
    {
        _offset = offset;
        Opacity = Math.Clamp(opacity, 0, 1);
        if (!_anchored) return;
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var x = _rest.X + _towardTaskbar.X * _slide * offset;
        var y = _rest.Y + _towardTaskbar.Y * _slide * offset;
        SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_widgetMode && !_exiting)
        {
            e.Cancel = true;
            HideFlyout();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopMotion();
        _missingArtworkTimer.Stop();
        base.OnClosed(e);
    }

    // What changed on screen is what moves: a new track brings its title in from the side that was skipped to and
    // dissolves to its cover and colours, play and pause swap with a small pop, and the progress glides between the
    // once-a-second position updates.
    private void Media_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MediaSessionService.CurrentMedia) || sender is not MediaSessionService media) return;
        var before = _lastMedia;
        var now = media.CurrentMedia;
        ShowProgress(now, before);
        UpdateArtwork(now);
        _lastMedia = now;
        var animate = before is not null && IsVisible;
        if (before is null || before.IsPlaying != now.IsPlaying) ShowPlayState(now.IsPlaying, animate);
        if (!animate) return;

        if (before!.Title != now.Title || before.Artist != now.Artist)
        {
            var direction = _sinceSkip.IsRunning && _sinceSkip.Elapsed < SkipMemory ? _skipDirection : 1;
            _sinceSkip.Reset();
            Motion.Enter(TrackText, dx: 16 * direction, milliseconds: Motion.Slow);
        }
    }

    // Opening: the cover settles first and the rest follows a beat behind, top to bottom.
    private void PlayEntrance()
    {
        Motion.Enter(ArtworkFrame, scale: 0.94, milliseconds: Motion.Slow);
        Motion.Enter(TrackText, dy: 6, milliseconds: Motion.Slow, delay: 40);
        Motion.Enter(TransportControls, dy: 6, milliseconds: Motion.Slow, delay: 80);
        Motion.Enter(Timeline, dy: 4, milliseconds: Motion.Slow, delay: 110);
    }

    private void UpdateArtwork(MediaInfo now)
    {
        if (now.Artwork is not null)
        {
            _missingArtworkTimer.Stop();
            if (!ReferenceEquals(now.Artwork, _shownArtwork)) ShowArtwork(now.Artwork, IsVisible);
        }
        else if (!ReferenceEquals(_shownArtwork, _fallbackArtwork) && !_missingArtworkTimer.IsEnabled)
        {
            _missingArtworkTimer.Start();
        }
    }

    // The new cover dissolves in over the old one, settling from a slight zoom when it belongs to a new track; the
    // ambient light and the accent follow it more slowly. Apps often send the same cover again for the same track,
    // which then just quietly replaces itself.
    private void ShowArtwork(ImageSource artwork, bool animate)
    {
        var title = (DataContext as MediaSessionService)?.CurrentMedia.Title;
        var newTrack = title != _shownArtworkTitle;
        _shownArtwork = artwork;
        _shownArtworkTitle = title;

        ArtworkPrevious.Source = animate ? ArtworkCurrent.Source : null;
        AmbientPrevious.Source = animate ? AmbientCurrent.Source : null;
        ArtworkCurrent.Source = artwork;
        AmbientCurrent.Source = artwork;

        var palette = ReferenceEquals(artwork, _fallbackArtwork) ? ArtworkPalette.Default : ArtworkPalette.From(artwork);
        var clear = Color.FromArgb(0, palette.GlowSecondary.R, palette.GlowSecondary.G, palette.GlowSecondary.B);
        Motion.ColorTo(_accent, palette.Accent, animate, ArtworkDissolve);
        Motion.ColorTo(_glowCenter, palette.Glow, animate, AmbientDissolve);
        Motion.ColorTo(_glowMiddle, palette.GlowSecondary, animate, AmbientDissolve);
        Motion.ColorTo(_glowEdge, clear, animate, AmbientDissolve);

        if (!animate || !Motion.Enabled)
        {
            Motion.Reset(ArtworkCurrent);
            Motion.Reset(AmbientCurrent);
            ArtworkPrevious.Source = AmbientPrevious.Source = null;
            return;
        }
        Motion.Reset(ArtworkPrevious);
        Motion.Reset(AmbientPrevious);
        Motion.Enter(ArtworkCurrent, scale: newTrack ? 1.06 : 1, milliseconds: ArtworkDissolve);
        Motion.FadeTo(ArtworkPrevious, 0, ArtworkDissolve);
        Motion.Enter(AmbientCurrent, milliseconds: AmbientDissolve);
        Motion.FadeTo(AmbientPrevious, 0, AmbientDissolve);
    }

    private void ShowPlayState(bool playing, bool animate)
    {
        var (shown, hidden) = playing ? (PauseGlyph, PlayGlyph) : (PlayGlyph, PauseGlyph);
        if (animate)
        {
            Motion.Enter(shown, scale: 0.5);
            Motion.FadeTo(hidden, 0, Motion.Quick);
            return;
        }
        Motion.Reset(shown);
        Motion.Reset(hidden);
        hidden.Opacity = 0;
    }

    private void ShowProgress(MediaInfo now, MediaInfo? before)
    {
        Progress.ShowsPosition = now.DurationSeconds > 0;
        var from = Progress.Value;
        Progress.BeginAnimation(RangeBase.ValueProperty, null);
        Progress.Maximum = now.ProgressMaximum;
        Progress.Value = now.ProgressValue;

        // A normal one-second step is smoothed; a seek, a new track or a pause is shown as it is.
        var step = now.ProgressValue - from;
        var sameTrack = before is not null && before.Title == now.Title
            && Math.Abs(before.ProgressMaximum - now.ProgressMaximum) < 0.5;
        if (!Motion.Enabled || !IsVisible || !sameTrack || !now.IsPlaying || step <= 0 || step > 2.5) return;
        Progress.BeginAnimation(RangeBase.ValueProperty,
            new DoubleAnimation(from, now.ProgressValue, TimeSpan.FromSeconds(step)) { FillBehavior = FillBehavior.Stop });
    }

    private void Volume_Click(object sender, RoutedEventArgs e) => VolumeFlyoutPopup.Toggle();

    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        RememberSkip(-1);
        await ((MediaSessionService)DataContext).PreviousAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
        => await ((MediaSessionService)DataContext).PlayPauseAsync();

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        RememberSkip(1);
        await ((MediaSessionService)DataContext).NextAsync();
    }

    private void RememberSkip(int direction)
    {
        _skipDirection = direction;
        _sinceSkip.Restart();
    }

    // Buttons mark their own clicks as handled, so this only sees presses on the window's background.
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

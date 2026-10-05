using System.Windows;
using System.Windows.Media;
using FluentFlow.Services;

namespace FluentFlow.Controls;

// Draw all bars in one lightweight visual instead of updating 24 layout bindings every frame.
public sealed class AudioVisualizer : FrameworkElement
{
    public static readonly DependencyProperty ServiceProperty = DependencyProperty.Register(
        nameof(Service), typeof(AudioVisualizerService), typeof(AudioVisualizer),
        new PropertyMetadata(null, ServiceChanged));
    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(AudioVisualizer),
        new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty IsMirroredProperty = DependencyProperty.Register(
        nameof(IsMirrored), typeof(bool), typeof(AudioVisualizer),
        new PropertyMetadata(false, (sender, _) => ((AudioVisualizer)sender).DrawBars()));

    private readonly float[] _levels = new float[AudioVisualizerService.BarCount];
    private readonly float[] _previousLevels = new float[AudioVisualizerService.BarCount];
    private TimeSpan? _lastFrameTime;
    private TimeSpan? _nextFrameTime;
    private readonly DrawingVisual _drawing = new();
    private AudioVisualizerService? _observedService;
    private volatile bool _rendering;
    private volatile bool _visible;
    private int _wakePending;

    public AudioVisualizer()
    {
        AddVisualChild(_drawing);
        SizeChanged += (_, _) => DrawBars();
        Loaded += (_, _) =>
        {
            _visible = IsVisible;
            ObserveService();
            DrawBars();
        };
        Unloaded += (_, _) =>
        {
            _visible = false;
            StopRendering();
            if (_observedService is not null) _observedService.SpectrumAvailable -= SpectrumAvailable;
            _observedService = null;
        };
        IsVisibleChanged += (_, _) =>
        {
            _visible = IsVisible;
            if (_visible && IsLoaded && Service is not null) StartRendering();
            else StopRendering();
        };
    }

    public AudioVisualizerService? Service
    {
        get => (AudioVisualizerService?)GetValue(ServiceProperty);
        set => SetValue(ServiceProperty, value);
    }

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    // Bars grow up and down from the middle line instead of rising from the bottom.
    public bool IsMirrored
    {
        get => (bool)GetValue(IsMirroredProperty);
        set => SetValue(IsMirroredProperty, value);
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index)
        => index == 0 ? _drawing : throw new ArgumentOutOfRangeException(nameof(index));

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == BarBrushProperty && _drawing is not null) DrawBars();
    }

    private static void ServiceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var visualizer = (AudioVisualizer)sender;
        if (visualizer.IsLoaded) visualizer.ObserveService();
    }

    private void ObserveService()
    {
        StopRendering();
        if (_observedService is not null) _observedService.SpectrumAvailable -= SpectrumAvailable;
        _observedService = Service;
        if (_observedService is not null)
        {
            _observedService.SpectrumAvailable += SpectrumAvailable;
            StartRendering();
        }
    }

    private void SpectrumAvailable(object? sender, EventArgs args)
    {
        if (!_visible || _rendering || Dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _wakePending, 1) != 0) return;
        Dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref _wakePending, 0);
            if (IsLoaded && IsVisible && ReferenceEquals(sender, _observedService)) StartRendering();
        });
    }

    private void StartRendering()
    {
        if (_rendering) return;
        _rendering = true;
        _lastFrameTime = null;
        _nextFrameTime = null;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        _rendering = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs args)
    {
        if (!IsVisible || Service is null || args is not RenderingEventArgs rendering) return;
        if (_nextFrameTime.HasValue && rendering.RenderingTime < _nextFrameTime.Value) return;
        var elapsed = _lastFrameTime.HasValue
            ? (rendering.RenderingTime - _lastFrameTime.Value).TotalSeconds
            : 1.0 / 60;
        // Keep a 60 Hz schedule even on 120/144 Hz displays; don't accumulate missed frames.
        var frameInterval = TimeSpan.FromSeconds(1.0 / 60);
        _nextFrameTime = (_nextFrameTime ?? rendering.RenderingTime) + frameInterval;
        if (_nextFrameTime < rendering.RenderingTime) _nextFrameTime = rendering.RenderingTime + frameInterval;
        _lastFrameTime = rendering.RenderingTime;
        Service.GetSmoothedLevels(_levels, elapsed);

        var changed = false;
        var allAtMinimum = true;
        for (var i = 0; i < _levels.Length; i++)
        {
            changed |= Math.Abs(_levels[i] - _previousLevels[i]) > 0.0003f;
            allAtMinimum &= _levels[i] == 0;
        }
        if (changed)
        {
            DrawBars();
            Array.Copy(_levels, _previousLevels, _levels.Length);
        }
        if (allAtMinimum) StopRendering();
    }

    private void DrawBars()
    {
        // Updating this child visual avoids running layout again for each animation frame.
        using var drawingContext = _drawing.RenderOpen();
        var slotWidth = ActualWidth / _levels.Length;
        var barWidth = slotWidth * 0.6;
        var minimumHeight = Math.Min(2, ActualHeight);
        for (var i = 0; i < _levels.Length; i++)
        {
            var height = minimumHeight + _levels[i] * (ActualHeight - minimumHeight);
            var top = IsMirrored ? (ActualHeight - height) / 2 : ActualHeight - height;
            drawingContext.DrawRoundedRectangle(BarBrush, null,
                new Rect(i * slotWidth, top, barWidth, height), Math.Min(1.5, barWidth / 2), Math.Min(1.5, barWidth / 2));
        }
    }
}

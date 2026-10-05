using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FluentFlow.Services;

// Owns system-output capture, FFT targets and smoothing. Audio is analyzed only in memory.
public sealed class AudioVisualizerService : INotifyPropertyChanged, IAsyncDisposable
{
    public const int BarCount = 24;
    private const double SilenceTimeoutSeconds = 0.18;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly object _levelsLock = new();
    private readonly float[] _targets = new float[BarCount];
    private readonly float[] _smoothed = new float[BarCount];
    private MMDeviceEnumerator? _devices;
    private MMDeviceNotificationClient? _notifications;
    private WasapiRecorder? _capture;
    private CaptureDataAvailableHandler? _dataHandler;
    private EventHandler<StoppedEventArgs>? _stoppedHandler;
    private long _lastPacketTimestamp;
    private int _captureVersion;
    private bool _started;
    private volatile bool _disposed;
    private string _statusMessage = "Waiting for system output audio";

    public AudioVisualizerService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public string StatusMessage => _statusMessage;
    public event PropertyChangedEventHandler? PropertyChanged;
    // Raised on the audio thread; the view uses it only to wake an idle renderer.
    public event EventHandler? SpectrumAvailable;

    public async Task StartAsync()
    {
        if (_started || _disposed) return;
        _started = true;
        await RestartCaptureAsync().ConfigureAwait(false);
    }

    private async Task RestartCaptureAsync()
    {
        await _captureGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            await ReleaseCaptureAsync().ConfigureAwait(false);
            await Task.Run(() =>
            {
                if (_devices is null)
                {
                    _devices = new MMDeviceEnumerator();
                    _notifications = _devices.CreateNotificationClient(useSynchronizationContext: false);
                    _notifications.DefaultDeviceChanged += DefaultDeviceChanged;
                }

                var capture = new WasapiRecorderBuilder()
                    .WithLoopbackCapture()
                    .WithBufferLength(20)
                    .Build();
                _capture = capture;
                var analyzer = new AudioSpectrumAnalyzer(capture.WaveFormat, BarCount);
                var version = Volatile.Read(ref _captureVersion);
                _dataHandler = (buffer, flags, _, _) => ProcessAudio(analyzer, version, buffer, flags);
                _stoppedHandler = (_, args) =>
                {
                    if (_disposed || version != Volatile.Read(ref _captureVersion)) return;
                    ClearTargets();
                    if (args.Exception is not null)
                    {
                        Debug.WriteLine($"Audio capture stopped: {args.Exception}");
                        SetStatus("Audio capture unavailable — check the output device");
                    }
                };
                capture.DataAvailable += _dataHandler;
                capture.RecordingStopped += _stoppedHandler;
                capture.StartRecording();
                SetStatus($"System output audio · {capture.DeviceFriendlyName}");
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Audio visualizer unavailable: {exception}");
            await ReleaseCaptureAsync().ConfigureAwait(false);
            SetStatus("Audio capture unavailable — check the output device");
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private void ProcessAudio(AudioSpectrumAnalyzer analyzer, int version,
        ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags)
    {
        if (_disposed || version != Volatile.Read(ref _captureVersion)) return;
        var now = Stopwatch.GetTimestamp();
        bool gap;
        lock (_levelsLock)
        {
            gap = _lastPacketTimestamp == 0
                || Stopwatch.GetElapsedTime(_lastPacketTimestamp, now).TotalSeconds > SilenceTimeoutSeconds;
            _lastPacketTimestamp = now;
        }

        if (gap || (flags & AudioClientBufferFlags.DataDiscontinuity) != 0)
            analyzer.Reset();
        if ((flags & AudioClientBufferFlags.Silent) != 0)
        {
            analyzer.Reset();
            ClearTargets();
            return;
        }

        var updated = analyzer.Process(buffer);
        if (updated || gap)
        {
            lock (_levelsLock) analyzer.Levels.CopyTo(_targets, 0);
            foreach (var level in analyzer.Levels)
            {
                if (level <= 0) continue;
                SpectrumAvailable?.Invoke(this, EventArgs.Empty);
                break;
            }
        }
    }

    // Called by the WPF renderer. Targets are thread-safe; smoothing belongs to the render thread.
    public void GetSmoothedLevels(Span<float> destination, double elapsedSeconds)
    {
        if (destination.Length != BarCount) throw new ArgumentException("Expected 24 visualizer levels", nameof(destination));
        var elapsed = Math.Clamp(elapsedSeconds, 0, 0.1);
        var attack = (float)(1 - Math.Exp(-elapsed / 0.045));
        var decay = (float)(1 - Math.Exp(-elapsed / 0.22));

        lock (_levelsLock)
        {
            // WASAPI may stop sending packets completely during silence.
            var silent = _disposed || _lastPacketTimestamp == 0
                || Stopwatch.GetElapsedTime(_lastPacketTimestamp).TotalSeconds > SilenceTimeoutSeconds;
            for (var i = 0; i < BarCount; i++)
            {
                var target = silent ? 0 : _targets[i];
                var amount = target > _smoothed[i] ? attack : decay;
                _smoothed[i] += (target - _smoothed[i]) * amount;
                if (_smoothed[i] < 0.002f) _smoothed[i] = 0;
                destination[i] = _smoothed[i];
            }
        }
    }

    private void DefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs args)
    {
        // The default multimedia render endpoint is the device loopback uses.
        if (!_disposed && args.Flow == DataFlow.Render && args.Role == Role.Multimedia)
            _ = RestartCaptureAsync();
    }

    private void ClearTargets()
    {
        lock (_levelsLock)
        {
            Array.Clear(_targets);
            _lastPacketTimestamp = 0;
        }
    }

    private void SetStatus(string message)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.InvokeAsync(() =>
        {
            if (_disposed) return;
            _statusMessage = message;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
        });
    }

    private async Task ReleaseCaptureAsync()
    {
        Interlocked.Increment(ref _captureVersion);
        ClearTargets();
        var capture = _capture;
        _capture = null;
        if (capture is null) return;
        capture.DataAvailable -= _dataHandler;
        capture.RecordingStopped -= _stoppedHandler;
        try { await capture.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { Debug.WriteLine($"Audio device cleanup: {exception.Message}"); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _captureGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _notifications?.Dispose();
            _devices?.Dispose();
            await ReleaseCaptureAsync().ConfigureAwait(false);
        }
        finally
        {
            _captureGate.Release();
        }
    }
}

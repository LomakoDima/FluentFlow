using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace FluentFlow.Services;

// Controls the master volume of the current Windows output device and follows it when the default device changes.
public sealed class SystemVolumeService : INotifyPropertyChanged, IDisposable
{
    // Windows echoes our own writes back as notifications, often slightly late while a slider is dragged.
    private const double EchoGuardSeconds = 0.25;
    private readonly Dispatcher _dispatcher;
    private MMDeviceEnumerator? _devices;
    private MMDeviceNotificationClient? _notifications;
    private MMDevice? _device;
    private AudioEndpointVolume? _endpointVolume;
    private double _volume;
    private bool _isMuted;
    private bool _hasDevice;
    private string _deviceName = "No output device";
    private long _lastUserWrite;
    private bool _disposed;

    public SystemVolumeService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    // The setter is the user path (slider, wheel). Values coming from Windows never go through it.
    public double Volume
    {
        get => _volume;
        set
        {
            // Windows works in whole percents; matching it keeps the slider and the device identical.
            var next = Math.Round(Math.Clamp(value, 0, 1), 2);
            if (!HasDevice || _volume == next) return;
            _lastUserWrite = Stopwatch.GetTimestamp();
            SetVolumeValue(next);
            try
            {
                if (_disposed || _endpointVolume is null) return;
                _endpointVolume.MasterVolumeLevelScalar = (float)next;
                // Like the Windows flyout: raising the volume while muted unmutes.
                if (next > 0 && _endpointVolume.Mute)
                {
                    _endpointVolume.Mute = false;
                    IsMuted = false;
                }
            }
            catch (Exception exception) { Debug.WriteLine($"Could not set Windows volume: {exception.Message}"); }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set
        {
            if (_isMuted == value) return;
            _isMuted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeIcon));
            OnPropertyChanged(nameof(VolumeDescription));
        }
    }

    public bool HasDevice
    {
        get => _hasDevice;
        private set
        {
            if (_hasDevice == value) return;
            _hasDevice = value;
            OnPropertyChanged();
        }
    }

    public string DeviceName
    {
        get => _deviceName;
        private set
        {
            if (_deviceName == value) return;
            _deviceName = value;
            OnPropertyChanged();
        }
    }

    public string? CurrentDeviceId { get; private set; }
    public int VolumePercent => (int)Math.Round(Volume * 100);

    // Segoe Fluent Icons: mute, then volume with 1..3 waves.
    public string VolumeIcon => !HasDevice || IsMuted || VolumePercent == 0 ? ""
        : VolumePercent <= 33 ? ""
        : VolumePercent <= 66 ? ""
        : "";

    public string VolumeDescription => !HasDevice ? "No output device"
        : IsMuted ? "Volume: muted"
        : $"Volume: {VolumePercent}%";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Initialize()
    {
        if (_disposed || _devices is not null) return;
        try
        {
            _devices = new MMDeviceEnumerator();
            // These callbacks are marshalled back to WPF's DispatcherSynchronizationContext.
            _notifications = _devices.CreateNotificationClient();
            _notifications.DefaultDeviceChanged += DefaultDeviceChanged;
            _notifications.DeviceRemoved += DeviceRemoved;
            _notifications.DeviceStateChanged += DeviceStateChanged;
            ConnectToDefaultDevice();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"System volume control is unavailable: {exception}");
            OnPropertyChanged(nameof(VolumeDescription));
        }
    }

    public void ToggleMute()
    {
        try
        {
            if (!_disposed && _endpointVolume is not null)
            {
                _endpointVolume.Mute = !_endpointVolume.Mute;
                IsMuted = _endpointVolume.Mute;
            }
        }
        catch (Exception exception) { Debug.WriteLine($"Could not toggle Windows mute: {exception.Message}"); }
    }

    private void ConnectToDefaultDevice(bool force = false)
    {
        if (_disposed || _devices is null) return;
        MMDevice? nextDevice = null;
        AudioEndpointVolume? nextEndpointVolume = null;
        try
        {
            if (!_devices.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out nextDevice))
            {
                // Last output device unplugged or disabled: keep the UI honest instead of showing stale values.
                DisconnectDevice();
                return;
            }

            // Windows reports the default change once per role; reconnect only when the device really changed.
            if (!force && _device is not null && nextDevice.ID == CurrentDeviceId)
            {
                nextDevice.Dispose();
                return;
            }

            nextEndpointVolume = nextDevice.AudioEndpointVolume;
            nextEndpointVolume.OnVolumeNotification += VolumeChanged;
            ReleaseDevice();
            _device = nextDevice;
            _endpointVolume = nextEndpointVolume;
            CurrentDeviceId = nextDevice.ID;
            DeviceName = nextDevice.FriendlyName;
            HasDevice = true;
            SetVolumeValue(Math.Round(_endpointVolume.MasterVolumeLevelScalar, 2));
            IsMuted = _endpointVolume.Mute;
        }
        catch (Exception exception)
        {
            nextEndpointVolume?.Dispose();
            nextDevice?.Dispose();
            Debug.WriteLine($"Could not connect to the default Windows audio device: {exception}");
            DisconnectDevice();
        }
    }

    private void DisconnectDevice()
    {
        ReleaseDevice();
        CurrentDeviceId = null;
        DeviceName = "No output device";
        HasDevice = false;
        SetVolumeValue(0);
        IsMuted = false;
    }

    private void ReleaseDevice()
    {
        var endpointVolume = _endpointVolume;
        _endpointVolume = null;
        if (endpointVolume is not null)
        {
            endpointVolume.OnVolumeNotification -= VolumeChanged;
            endpointVolume.Dispose();
        }
        _device?.Dispose();
        _device = null;
    }

    // Updates the cached value and the properties derived from it, without touching the device.
    private void SetVolumeValue(double value)
    {
        if (_volume == value) return;
        _volume = value;
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(VolumeIcon));
        OnPropertyChanged(nameof(VolumeDescription));
    }

    private void VolumeChanged(AudioVolumeNotificationData data)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.InvokeAsync(() =>
        {
            if (_disposed || _endpointVolume is null) return;
            // Read current values from the default endpoint instead of displaying a stale notification.
            try
            {
                if (Stopwatch.GetElapsedTime(_lastUserWrite).TotalSeconds > EchoGuardSeconds)
                    SetVolumeValue(Math.Round(_endpointVolume.MasterVolumeLevelScalar, 2));
                IsMuted = _endpointVolume.Mute;
            }
            catch (Exception exception) { Debug.WriteLine($"Could not read Windows volume: {exception.Message}"); }
        });
    }

    private void DefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs args)
    {
        if (!_disposed && args.Flow == DataFlow.Render && args.Role == Role.Multimedia)
            ConnectToDefaultDevice();
    }

    private void DeviceRemoved(object? sender, DeviceNotificationEventArgs args)
    {
        if (_disposed) return;
        // The current device may vanish before Windows names a new default.
        if (args.DeviceId == CurrentDeviceId) ConnectToDefaultDevice(force: true);
    }

    private void DeviceStateChanged(object? sender, DeviceStateChangedEventArgs args)
    {
        if (_disposed) return;
        if (args.DeviceId == CurrentDeviceId && args.NewState != DeviceState.Active)
            ConnectToDefaultDevice(force: true);
        else if (CurrentDeviceId is null && args.NewState == DeviceState.Active)
            ConnectToDefaultDevice();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_notifications is not null)
        {
            _notifications.DefaultDeviceChanged -= DefaultDeviceChanged;
            _notifications.DeviceRemoved -= DeviceRemoved;
            _notifications.DeviceStateChanged -= DeviceStateChanged;
            _notifications.Dispose();
            _notifications = null;
        }
        ReleaseDevice();
        _devices?.Dispose();
        _devices = null;
    }
}

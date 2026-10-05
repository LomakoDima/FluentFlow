using System.Buffers.Binary;
using NAudio.Dsp;
using NAudio.Wave;

namespace FluentFlow.Services;

// Pure signal processing: no Windows device or WPF objects. Buffers are reused for every FFT.
internal sealed class AudioSpectrumAnalyzer
{
    private const int FftSize = 4096;
    private const int FftExponent = 12; // 2^12 = 4096
    private readonly WaveFormat _format;
    private readonly bool _isFloat;
    private readonly int _bytesPerSample;
    private readonly int _hopSize;
    private readonly float[] _samples;
    private readonly float[] _window = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly float[] _power = new float[FftSize / 2];
    private readonly int[] _bandStarts;
    private readonly int[] _bandEnds;
    private int _writeIndex;
    private int _sampleCount;
    private int _framesSinceFft;

    public AudioSpectrumAnalyzer(WaveFormat format, int bandCount)
    {
        _format = format is WaveFormatExtensible extensible ? extensible.ToStandardWaveFormat() : format;
        _isFloat = _format.Encoding == WaveFormatEncoding.IeeeFloat;
        if ((_isFloat && _format.BitsPerSample != 32)
            || (!_isFloat && (_format.Encoding != WaveFormatEncoding.Pcm
                || _format.BitsPerSample is not (8 or 16 or 24 or 32))))
            throw new NotSupportedException($"Unsupported output audio format: {format}");

        _bytesPerSample = _format.BitsPerSample / 8;
        _samples = new float[FftSize * _format.Channels];
        _hopSize = Math.Max(FftSize / 4, _format.SampleRate / 60);
        Levels = new float[bandCount];
        _bandStarts = new int[bandCount];
        _bandEnds = new int[bandCount];

        for (var i = 0; i < FftSize; i++)
            _window[i] = (float)FastFourierTransform.HannWindow(i, FftSize);

        // Logarithmic bands: bass on the left, treble on the right.
        const double minimumFrequency = 40;
        var maximumFrequency = Math.Min(16000, _format.SampleRate / 2.0);
        var binWidth = _format.SampleRate / (double)FftSize;
        for (var band = 0; band < bandCount; band++)
        {
            var low = minimumFrequency * Math.Pow(maximumFrequency / minimumFrequency, band / (double)bandCount);
            var high = minimumFrequency * Math.Pow(maximumFrequency / minimumFrequency, (band + 1.0) / bandCount);
            _bandStarts[band] = Math.Clamp((int)Math.Floor(low / binWidth), 1, _power.Length - 1);
            _bandEnds[band] = Math.Clamp((int)Math.Ceiling(high / binWidth), _bandStarts[band] + 1, _power.Length);
        }
    }

    public float[] Levels { get; }

    public void Reset()
    {
        _writeIndex = 0;
        _sampleCount = 0;
        _framesSinceFft = 0;
        Array.Clear(Levels);
    }

    public bool Process(ReadOnlySpan<byte> buffer)
    {
        var updated = false;
        for (var offset = 0; offset + _format.BlockAlign <= buffer.Length; offset += _format.BlockAlign)
        {
            for (var channel = 0; channel < _format.Channels; channel++)
            {
                var sample = ReadSample(buffer.Slice(offset + channel * _bytesPerSample, _bytesPerSample));
                _samples[_writeIndex * _format.Channels + channel] = float.IsFinite(sample) ? sample : 0;
            }

            _writeIndex = (_writeIndex + 1) % FftSize;
            _sampleCount = Math.Min(_sampleCount + 1, FftSize);
            _framesSinceFft++;
            if (_sampleCount == FftSize && _framesSinceFft >= _hopSize)
            {
                CalculateSpectrum();
                _framesSinceFft = 0;
                updated = true;
            }
        }
        return updated;
    }

    private float ReadSample(ReadOnlySpan<byte> bytes)
    {
        if (_isFloat) return BinaryPrimitives.ReadSingleLittleEndian(bytes);
        return _format.BitsPerSample switch
        {
            8 => (bytes[0] - 128) / 128f,
            16 => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768f,
            24 => ((bytes[0] | bytes[1] << 8 | bytes[2] << 16) << 8 >> 8) / 8388608f,
            32 => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648f,
            _ => 0
        };
    }

    private void CalculateSpectrum()
    {
        Array.Clear(_power);
        // Combine channel POWER rather than samples, so opposite-phase stereo cannot cancel out.
        for (var channel = 0; channel < _format.Channels; channel++)
        {
            double mean = 0;
            for (var i = 0; i < FftSize; i++) mean += _samples[i * _format.Channels + channel];
            mean /= FftSize;

            for (var i = 0; i < FftSize; i++)
            {
                var sampleIndex = (_writeIndex + i) % FftSize;
                _fft[i].X = (float)(_samples[sampleIndex * _format.Channels + channel] - mean) * _window[i];
                _fft[i].Y = 0;
            }
            FastFourierTransform.FFT(true, FftExponent, _fft);
            for (var bin = 1; bin < _power.Length; bin++)
                _power[bin] += (_fft[bin].X * _fft[bin].X + _fft[bin].Y * _fft[bin].Y) / _format.Channels;
        }

        for (var band = 0; band < Levels.Length; band++)
        {
            float peakPower = 0;
            for (var bin = _bandStarts[band]; bin < _bandEnds[band]; bin++)
                peakPower = Math.Max(peakPower, _power[bin]);

            // NAudio's forward FFT is normalized; factor 4 compensates for the Hann window.
            var amplitude = 4 * Math.Sqrt(peakPower);
            var decibels = 20 * Math.Log10(Math.Max(amplitude, 1e-12));
            Levels[band] = (float)Math.Clamp((decibels + 65) / 53, 0, 1);
        }
    }
}

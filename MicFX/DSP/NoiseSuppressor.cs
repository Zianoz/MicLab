using System.Threading;
using NAudio.Wave;

namespace MicFX.DSP;

public sealed class NoiseSuppressor : ISampleProvider, IDisposable
{
    private const int RnNoiseSampleRate = 48000;
    private const int RnNoiseFrameSize = 480;
    private const float RnNoiseScale = 32768f;

    private readonly ISampleProvider _source;
    private readonly object _stateLock = new();

    private float[] _sourceBuffer = new float[RnNoiseFrameSize];
    private readonly float[] _inputFrame = new float[RnNoiseFrameSize];
    private readonly float[] _scaledInputFrame = new float[RnNoiseFrameSize];
    private readonly float[] _scaledOutputFrame = new float[RnNoiseFrameSize];
    private readonly float[] _outputQueue = new float[RnNoiseFrameSize * 8];

    private IntPtr _denoiseState;
    private int _inputFrameCount;
    private int _outputReadIndex;
    private int _outputWriteIndex;
    private int _outputCount;
    private bool _disposed;

    private bool _enabled = true;
    private float _strength = 0.85f;
    private int _resetRequested = 1;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public NoiseSuppressor(ISampleProvider source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));

        if (_source.WaveFormat.SampleRate != RnNoiseSampleRate)
            throw new InvalidOperationException("RNNoise requires a 48kHz pipeline. Configure the MicFX audio pipeline to run at 48000 Hz.");

        if (_source.WaveFormat.Channels != 1)
            throw new InvalidOperationException("RNNoise requires a mono pipeline. Configure the MicFX audio pipeline to run in mono at 48kHz.");

        ResetStateUnsafe();
    }

    ~NoiseSuppressor()
    {
        Dispose(false);
    }

    public void ApplyParams(bool enabled, float strength)
    {
        var previousEnabled = Volatile.Read(ref _enabled);
        Volatile.Write(ref _enabled, enabled);
        Volatile.Write(ref _strength, Math.Clamp(strength, 0f, 1f));

        if (previousEnabled != enabled)
            Volatile.Write(ref _resetRequested, 1);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (buffer.Length - offset < count)
            throw new ArgumentException("The destination buffer is too small for the requested read.", nameof(buffer));

        lock (_stateLock)
        {
            ThrowIfDisposed();

            if (Volatile.Read(ref _resetRequested) != 0)
                ResetStateUnsafe();

            if (!Volatile.Read(ref _enabled))
            {
                ResetBufferedAudioUnsafe();
                return _source.Read(buffer, offset, count);
            }

            int written = 0;
            while (written < count)
            {
                if (_outputCount > 0)
                {
                    written += DequeueSamples(buffer, offset + written, count - written);
                    continue;
                }

                int samplesNeeded = RnNoiseFrameSize - _inputFrameCount;
                if (_sourceBuffer.Length < samplesNeeded)
                    _sourceBuffer = new float[samplesNeeded];

                int read = _source.Read(_sourceBuffer, 0, samplesNeeded);
                if (read == 0)
                {
                    if (_inputFrameCount > 0)
                    {
                        EnqueuePartialDryFrameUnsafe();
                        continue;
                    }

                    break;
                }

                Array.Copy(_sourceBuffer, 0, _inputFrame, _inputFrameCount, read);
                _inputFrameCount += read;

                if (_inputFrameCount == RnNoiseFrameSize)
                    ProcessFrameUnsafe();
            }

            return written;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        lock (_stateLock)
        {
            if (_disposed)
                return;

            if (_denoiseState != IntPtr.Zero)
            {
                RNNoiseInterop.Destroy(_denoiseState);
                _denoiseState = IntPtr.Zero;
            }

            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ResetStateUnsafe()
    {
        if (_denoiseState != IntPtr.Zero)
            RNNoiseInterop.Destroy(_denoiseState);

        _denoiseState = RNNoiseInterop.Create();
        ResetBufferedAudioUnsafe();
        Volatile.Write(ref _resetRequested, 0);
    }

    private void ResetBufferedAudioUnsafe()
    {
        _inputFrameCount = 0;
        _outputReadIndex = 0;
        _outputWriteIndex = 0;
        _outputCount = 0;
        Array.Clear(_inputFrame);
        Array.Clear(_scaledInputFrame);
        Array.Clear(_scaledOutputFrame);
        Array.Clear(_outputQueue);
    }

    private void ProcessFrameUnsafe()
    {
        float strength = Volatile.Read(ref _strength);
        if (strength <= 0f)
        {
            EnqueueSamplesUnsafe(_inputFrame, RnNoiseFrameSize);
            _inputFrameCount = 0;
            return;
        }

        for (int i = 0; i < RnNoiseFrameSize; i++)
            _scaledInputFrame[i] = Math.Clamp(_inputFrame[i] * RnNoiseScale, -RnNoiseScale, RnNoiseScale);

        RNNoiseInterop.ProcessFrame(_denoiseState, _scaledOutputFrame, _scaledInputFrame);

        for (int i = 0; i < RnNoiseFrameSize; i++)
        {
            float dry = _inputFrame[i];
            float wet = _scaledOutputFrame[i] / RnNoiseScale;
            _scaledOutputFrame[i] = Math.Clamp((wet * strength) + (dry * (1f - strength)), -1f, 1f);
        }

        EnqueueSamplesUnsafe(_scaledOutputFrame, RnNoiseFrameSize);
        _inputFrameCount = 0;
    }

    private void EnqueuePartialDryFrameUnsafe()
    {
        EnqueueSamplesUnsafe(_inputFrame, _inputFrameCount);
        _inputFrameCount = 0;
    }

    private void EnqueueSamplesUnsafe(float[] samples, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _outputQueue[_outputWriteIndex] = samples[i];
            _outputWriteIndex = (_outputWriteIndex + 1) % _outputQueue.Length;

            if (_outputCount == _outputQueue.Length)
            {
                _outputReadIndex = (_outputReadIndex + 1) % _outputQueue.Length;
            }
            else
            {
                _outputCount++;
            }
        }
    }

    private int DequeueSamples(float[] destination, int offset, int count)
    {
        int toCopy = Math.Min(count, _outputCount);
        for (int i = 0; i < toCopy; i++)
        {
            destination[offset + i] = _outputQueue[_outputReadIndex];
            _outputReadIndex = (_outputReadIndex + 1) % _outputQueue.Length;
        }

        _outputCount -= toCopy;
        return toCopy;
    }
}

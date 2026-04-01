using System.Threading;
using NAudio.Wave;

namespace MicFX.DSP;

/// <summary>
/// Speech-aware expander that sits after RNNoise.
/// It scores each 10 ms frame using level and spectral brightness so nearby speech
/// opens the gate more reliably than distant muffled voices.
/// </summary>
public class NoiseGate : ISampleProvider
{
    private const int FrameSize = 480;

    private readonly ISampleProvider _source;
    private readonly object _stateLock = new();

    private float[] _sourceBuffer = new float[FrameSize];
    private readonly float[] _inputFrame = new float[FrameSize];
    private readonly float[] _outputQueue = new float[FrameSize * 8];

    private int _inputFrameCount;
    private int _outputReadIndex;
    private int _outputWriteIndex;
    private int _outputCount;

    private readonly float _sampleRate;

    private float _speechThreshold = 0.55f;
    private float _closeVoiceBias = 0.65f;
    private float _floorGain = DbToLinear(-18f);
    private float _attackCoeff;
    private float _releaseCoeff;
    private int _holdSamples;

    private float _gain = 1f;
    private int _holdCounter;
    private float _previousSample;
    private bool _enabled = true;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public NoiseGate(ISampleProvider source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sampleRate = source.WaveFormat.SampleRate;
        UpdateTimingCoefficients(8f, 140f, 180f);
    }

    public float ThresholdDb
    {
        set => Volatile.Write(ref _speechThreshold, LegacyThresholdToSpeechThreshold(value));
    }

    public void ApplyParams(float thresholdDb, float attackMs, float holdMs, float releaseMs)
    {
        ApplyParams(
            LegacyThresholdToSpeechThreshold(thresholdDb),
            Volatile.Read(ref _closeVoiceBias),
            LinearToDb(Volatile.Read(ref _floorGain)),
            attackMs,
            holdMs,
            releaseMs);
    }

    public void ApplyParams(float speechThreshold, float closeVoiceBias, float floorAttenuationDb, float attackMs, float holdMs, float releaseMs)
    {
        Volatile.Write(ref _speechThreshold, Math.Clamp(speechThreshold, 0f, 1f));
        Volatile.Write(ref _closeVoiceBias, Math.Clamp(closeVoiceBias, 0f, 1f));
        Volatile.Write(ref _floorGain, DbToLinear(-MathF.Abs(floorAttenuationDb)));
        UpdateTimingCoefficients(attackMs, holdMs, releaseMs);
    }

    public void SetEnabled(bool enabled)
    {
        Volatile.Write(ref _enabled, enabled);
    }

    public void SetTimings(float attackMs, float holdMs, float releaseMs)
    {
        UpdateTimingCoefficients(attackMs, holdMs, releaseMs);
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
            if (!Volatile.Read(ref _enabled))
            {
                _inputFrameCount = 0;
                _outputReadIndex = 0;
                _outputWriteIndex = 0;
                _outputCount = 0;
                _holdCounter = 0;
                _gain = 1f;
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

                int samplesNeeded = FrameSize - _inputFrameCount;
                if (_sourceBuffer.Length < samplesNeeded)
                    _sourceBuffer = new float[samplesNeeded];

                int read = _source.Read(_sourceBuffer, 0, samplesNeeded);
                if (read == 0)
                {
                    if (_inputFrameCount > 0)
                    {
                        ProcessBufferedFrame(_inputFrameCount);
                        _inputFrameCount = 0;
                        continue;
                    }

                    break;
                }

                Array.Copy(_sourceBuffer, 0, _inputFrame, _inputFrameCount, read);
                _inputFrameCount += read;

                if (_inputFrameCount == FrameSize)
                {
                    ProcessBufferedFrame(FrameSize);
                    _inputFrameCount = 0;
                }
            }

            return written;
        }
    }

    private void UpdateTimingCoefficients(float attackMs, float holdMs, float releaseMs)
    {
        attackMs = MathF.Max(attackMs, 1f);
        releaseMs = MathF.Max(releaseMs, 1f);
        holdMs = MathF.Max(holdMs, 0f);

        Volatile.Write(ref _attackCoeff, 1f - MathF.Exp(-1f / (_sampleRate * attackMs / 1000f)));
        Volatile.Write(ref _releaseCoeff, 1f - MathF.Exp(-1f / (_sampleRate * releaseMs / 1000f)));
        Volatile.Write(ref _holdSamples, (int)MathF.Round(_sampleRate * holdMs / 1000f));
    }

    private void ProcessBufferedFrame(int sampleCount)
    {
        float speechThreshold = Volatile.Read(ref _speechThreshold);
        float closeVoiceBias = Volatile.Read(ref _closeVoiceBias);
        float floorGain = Volatile.Read(ref _floorGain);
        float attackCoeff = Volatile.Read(ref _attackCoeff);
        float releaseCoeff = Volatile.Read(ref _releaseCoeff);
        int holdSamples = Volatile.Read(ref _holdSamples);

        float confidence = CalculateSpeechConfidence(sampleCount, closeVoiceBias);
        bool isSpeechFrame = confidence >= speechThreshold;

        for (int i = 0; i < sampleCount; i++)
        {
            if (isSpeechFrame)
            {
                _holdCounter = holdSamples;
                _gain += attackCoeff * (1f - _gain);
            }
            else if (_holdCounter > 0)
            {
                _holdCounter--;
                _gain += attackCoeff * (1f - _gain);
            }
            else
            {
                _gain += releaseCoeff * (floorGain - _gain);
            }

            _inputFrame[i] = _inputFrame[i] * _gain;
        }

        EnqueueSamplesUnsafe(_inputFrame, sampleCount);
    }

    private float CalculateSpeechConfidence(int sampleCount, float closeVoiceBias)
    {
        const float epsilon = 1e-9f;

        float sumSquares = 0f;
        float diffSquares = 0f;
        float peak = 0f;
        float previous = _previousSample;

        for (int i = 0; i < sampleCount; i++)
        {
            float sample = _inputFrame[i];
            float abs = MathF.Abs(sample);
            sumSquares += sample * sample;
            float diff = sample - previous;
            diffSquares += diff * diff;
            peak = MathF.Max(peak, abs);
            previous = sample;
        }

        _previousSample = previous;

        float rms = MathF.Sqrt(sumSquares / Math.Max(sampleCount, 1));
        float levelDb = 20f * MathF.Log10(rms + epsilon);
        float levelScore = Math.Clamp((levelDb + 55f) / 25f, 0f, 1f);

        float spectralFlux = MathF.Sqrt(diffSquares / Math.Max(sampleCount, 1));
        float brightness = Math.Clamp(spectralFlux / (rms + 0.02f), 0f, 1.2f) / 1.2f;
        float crest = Math.Clamp((peak / (rms + 0.001f) - 1f) / 5f, 0f, 1f);

        float proximityScore = Math.Clamp((brightness * 0.75f) + (crest * 0.25f), 0f, 1f);
        float baseConfidence = Math.Clamp((levelScore * 0.7f) + (proximityScore * 0.3f), 0f, 1f);

        return Math.Clamp(baseConfidence * ((1f - closeVoiceBias) + (proximityScore * closeVoiceBias)), 0f, 1f);
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

    private static float DbToLinear(float db) => MathF.Pow(10f, db / 20f);

    private static float LinearToDb(float linear) => 20f * MathF.Log10(MathF.Max(linear, 1e-6f));

    private static float LegacyThresholdToSpeechThreshold(float thresholdDb)
        => Math.Clamp((thresholdDb + 70f) / 70f, 0f, 1f);
}

using NAudio.Wave;

namespace MicFX.DSP;

/// <summary>
/// Noise gate. Attenuates signal when level drops below threshold.
/// Uses smooth attack/hold/release envelope to avoid clicks.
/// </summary>
public class NoiseGate : ISampleProvider
{
    private readonly ISampleProvider _source;

    // Parameters (set from UI thread, read from audio thread — using volatile)
    private volatile float _thresholdLinear = DbToLinear(-40f);
    private volatile float _attackCoeff;
    private volatile float _releaseCoeff;
    private volatile float _holdSamples;

    private float _sampleRate;
    private float _gain = 0f; // current gate gain (0 = closed, 1 = open)
    private float _holdCounter;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public NoiseGate(ISampleProvider source)
    {
        _source = source;
        _sampleRate = source.WaveFormat.SampleRate;
        UpdateCoeffs(10f, 100f, 100f);
    }

    public float ThresholdDb
    {
        set => _thresholdLinear = DbToLinear(value);
    }

    public void ApplyParams(float thresholdDb, float attackMs, float holdMs, float releaseMs)
    {
        _thresholdLinear = DbToLinear(thresholdDb);
        UpdateCoeffs(attackMs, holdMs, releaseMs);
    }

    public void SetTimings(float attackMs, float holdMs, float releaseMs)
    {
        _sampleRate = WaveFormat.SampleRate;
        UpdateCoeffs(attackMs, holdMs, releaseMs);
    }

    private void UpdateCoeffs(float attackMs, float holdMs, float releaseMs)
    {
        _attackCoeff = 1f - MathF.Exp(-1f / (_sampleRate * attackMs / 1000f));
        _releaseCoeff = 1f - MathF.Exp(-1f / (_sampleRate * releaseMs / 1000f));
        _holdSamples = _sampleRate * holdMs / 1000f;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        float threshold = _thresholdLinear;
        float attackC = _attackCoeff;
        float releaseC = _releaseCoeff;
        float holdS = _holdSamples;

        for (int i = 0; i < read; i++)
        {
            float sample = buffer[offset + i];
            float level = Math.Abs(sample);

            if (level >= threshold)
            {
                // Opening gate
                _holdCounter = holdS;
                _gain += attackC * (1f - _gain);
            }
            else if (_holdCounter > 0)
            {
                _holdCounter--;
                // hold: keep current gain
            }
            else
            {
                // Closing gate
                _gain += releaseC * (0f - _gain);
            }

            buffer[offset + i] = sample * _gain;
        }

        return read;
    }

    private static float DbToLinear(float db) => MathF.Pow(10f, db / 20f);
}

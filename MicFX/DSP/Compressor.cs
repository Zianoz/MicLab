using NAudio.Wave;

namespace MicFX.DSP;

/// <summary>
/// Dynamic range compressor. RMS-based level detection with smooth attack/release.
/// </summary>
public class Compressor : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float _sampleRate;

    // Parameters — volatile for thread-safe cross-thread reads
    private volatile float _thresholdLinear = DbToLinear(-18f);
    private volatile float _ratio = 4f;
    private volatile float _makeupLinear = 1f;
    private volatile float _attackCoeff;
    private volatile float _releaseCoeff;

    // RMS state
    private float _rmsLevel;
    private float _gainReduction = 1f; // current gain being applied

    private const int RmsWindowSize = 512;
    private float _sumSq;
    private int _rmsIndex;
    private readonly float[] _rmsBuffer;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public Compressor(ISampleProvider source)
    {
        _source = source;
        _sampleRate = source.WaveFormat.SampleRate;
        _rmsBuffer = new float[RmsWindowSize];
        UpdateCoeffs(10f, 100f);
    }

    public float ThresholdDb { set => _thresholdLinear = DbToLinear(value); }
    public float Ratio { set => _ratio = Math.Max(1f, value); }
    public float MakeupGainDb { set => _makeupLinear = DbToLinear(value); }

    public void ApplyParams(float thresholdDb, float ratio, float attackMs, float releaseMs, float makeupGainDb)
    {
        _thresholdLinear = DbToLinear(thresholdDb);
        _ratio = Math.Max(1f, ratio);
        _makeupLinear = DbToLinear(makeupGainDb);
        UpdateCoeffs(attackMs, releaseMs);
    }

    public void SetTimings(float attackMs, float releaseMs) => UpdateCoeffs(attackMs, releaseMs);

    private void UpdateCoeffs(float attackMs, float releaseMs)
    {
        _attackCoeff = 1f - MathF.Exp(-1f / (_sampleRate * attackMs / 1000f));
        _releaseCoeff = 1f - MathF.Exp(-1f / (_sampleRate * releaseMs / 1000f));
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        float threshold = _thresholdLinear;
        float ratio = _ratio;
        float makeup = _makeupLinear;
        float attackC = _attackCoeff;
        float releaseC = _releaseCoeff;

        for (int i = 0; i < read; i++)
        {
            float sample = buffer[offset + i];

            // Rolling RMS
            _sumSq -= _rmsBuffer[_rmsIndex];
            _rmsBuffer[_rmsIndex] = sample * sample;
            _sumSq += _rmsBuffer[_rmsIndex];
            _rmsIndex = (_rmsIndex + 1) % RmsWindowSize;
            _rmsLevel = MathF.Sqrt(_sumSq / RmsWindowSize);

            // Compute target gain
            float targetGain;
            if (_rmsLevel > threshold)
            {
                float excessDb = LinearToDb(_rmsLevel) - LinearToDb(threshold);
                float reductionDb = excessDb * (1f - 1f / ratio);
                targetGain = DbToLinear(-reductionDb);
            }
            else
            {
                targetGain = 1f;
            }

            // Smooth gain changes
            float coeff = targetGain < _gainReduction ? attackC : releaseC;
            _gainReduction += coeff * (targetGain - _gainReduction);

            buffer[offset + i] = sample * _gainReduction * makeup;
        }

        return read;
    }

    private static float DbToLinear(float db) => MathF.Pow(10f, db / 20f);
    private static float LinearToDb(float linear) => 20f * MathF.Log10(Math.Max(linear, 1e-10f));
}

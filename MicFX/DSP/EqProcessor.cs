using NAudio.Wave;

namespace MicFX.DSP;

/// <summary>
/// 10-band parametric EQ. Implements ISampleProvider so it slots into the NAudio chain.
/// Bands: 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 Hz.
/// Gain range: -12 to +12 dB per band. Q: 1.4 (fixed, adjustable later).
/// Thread-safe: gains are updated via BiQuadFilter's atomic coefficient swap.
/// </summary>
public class EqProcessor : ISampleProvider
{
    public static readonly float[] BandFrequencies = { 31f, 62f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };
    public const float DefaultQ = 1.4f;
    public const float MinGainDb = -12f;
    public const float MaxGainDb = 12f;

    private readonly ISampleProvider _source;
    private readonly BiQuadFilter[] _filters;
    private readonly float[] _gains;
    private float _sampleRate;

    public WaveFormat WaveFormat => _source.WaveFormat;
    public int BandCount => BandFrequencies.Length;

    public EqProcessor(ISampleProvider source)
    {
        _source = source;
        _sampleRate = source.WaveFormat.SampleRate;
        _filters = new BiQuadFilter[BandFrequencies.Length];
        _gains = new float[BandFrequencies.Length];

        for (int i = 0; i < _filters.Length; i++)
        {
            _filters[i] = new BiQuadFilter();
            // 0 dB = pass-through
            _filters[i].SetPeakingEQ(_sampleRate, BandFrequencies[i], DefaultQ, 0f);
        }
    }

    /// <summary>Set the gain for a band. Thread-safe — coefficients are swapped atomically.</summary>
    public void SetBandGain(int band, float gainDb)
    {
        if (band < 0 || band >= _filters.Length) return;
        gainDb = Math.Clamp(gainDb, MinGainDb, MaxGainDb);
        _gains[band] = gainDb;
        _filters[band].SetPeakingEQ(_sampleRate, BandFrequencies[band], DefaultQ, gainDb);
    }

    public float GetBandGain(int band) => band >= 0 && band < _gains.Length ? _gains[band] : 0f;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        int channels = WaveFormat.Channels;

        for (int i = 0; i < read; i++)
        {
            int ch = i % channels; // 0 = left/mono, 1 = right
            float sample = buffer[offset + i];
            foreach (var filter in _filters)
                sample = filter.Transform(sample, ch);
            buffer[offset + i] = sample;
        }

        return read;
    }
}

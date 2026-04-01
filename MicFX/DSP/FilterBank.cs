using NAudio.Wave;

namespace MicFX.DSP;

/// <summary>
/// Applies an independent high-pass and/or low-pass filter to the audio stream.
/// Both are 2nd-order Butterworth (Q = 0.7071).
/// Thread-safe: UI thread calls ApplyHpf/ApplyLpf, audio thread calls Read.
/// </summary>
public sealed class FilterBank : ISampleProvider
{
    private const float ButterworthQ = 0.7071f;

    private readonly ISampleProvider _source;
    private readonly BiQuadFilter _hpf = new();
    private readonly BiQuadFilter _lpf = new();

    // Volatile so the audio thread picks up changes without a lock
    private volatile bool _hpfEnabled;
    private volatile bool _lpfEnabled;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public FilterBank(ISampleProvider source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public void ApplyHpf(bool enabled, float cutoffHz)
    {
        _hpfEnabled = enabled;
        cutoffHz = Math.Clamp(cutoffHz, 10f, WaveFormat.SampleRate / 2f - 1f);
        _hpf.SetHighPass(WaveFormat.SampleRate, cutoffHz, ButterworthQ);
    }

    public void ApplyLpf(bool enabled, float cutoffHz)
    {
        _lpfEnabled = enabled;
        cutoffHz = Math.Clamp(cutoffHz, 10f, WaveFormat.SampleRate / 2f - 1f);
        _lpf.SetLowPass(WaveFormat.SampleRate, cutoffHz, ButterworthQ);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        bool hpf = _hpfEnabled;
        bool lpf = _lpfEnabled;

        if (!hpf && !lpf) return read;

        for (int i = 0; i < read; i++)
        {
            float s = buffer[offset + i];
            if (hpf) s = _hpf.Transform(s);
            if (lpf) s = _lpf.Transform(s);
            buffer[offset + i] = s;
        }

        return read;
    }
}

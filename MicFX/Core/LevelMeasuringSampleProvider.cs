using NAudio.Wave;

namespace MicFX.Core;

/// <summary>
/// Passes audio through unchanged while computing RMS and peak levels.
/// LevelMeasured is raised after each read (called from audio thread — marshal to UI thread yourself).
/// </summary>
public class LevelMeasuringSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Args: rms (0-1), peak (0-1)</summary>
    public event Action<float, float>? LevelMeasured;

    public LevelMeasuringSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read > 0)
            MeasureLevel(buffer, offset, read);
        return read;
    }

    private void MeasureLevel(float[] buffer, int offset, int count)
    {
        float sumSq = 0f;
        float peak = 0f;
        for (int i = offset; i < offset + count; i++)
        {
            float s = Math.Abs(buffer[i]);
            sumSq += s * s;
            if (s > peak) peak = s;
        }
        float rms = MathF.Sqrt(sumSq / count);
        LevelMeasured?.Invoke(rms, peak);
    }
}

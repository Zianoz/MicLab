using NAudio.Wave;

namespace MicFX.DSP;

/// <summary>
/// Chains EqProcessor → NoiseGate → Compressor in order.
/// All three processors are publicly accessible for parameter updates.
/// </summary>
public class EffectsChain : ISampleProvider
{
    public EqProcessor Eq { get; }
    public NoiseGate Gate { get; }
    public Compressor Compressor { get; }

    private readonly ISampleProvider _tail;

    public WaveFormat WaveFormat => _tail.WaveFormat;

    public EffectsChain(ISampleProvider source)
    {
        Eq = new EqProcessor(source);
        Gate = new NoiseGate(Eq);
        Compressor = new Compressor(Gate);
        _tail = Compressor;
    }

    public int Read(float[] buffer, int offset, int count)
        => _tail.Read(buffer, offset, count);
}

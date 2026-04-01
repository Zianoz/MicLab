using NAudio.Wave;

namespace MicFX.DSP;

/// <summary>
/// Chains EqProcessor → NoiseGate → Compressor in order.
/// All three processors are publicly accessible for parameter updates.
/// </summary>
public class EffectsChain : ISampleProvider
{
    public FilterBank       Filters          { get; }
    public EqProcessor      Eq               { get; }
    public NoiseSuppressor  NoiseSuppressor  { get; }
    public NoiseGate        Gate             { get; }
    public Compressor       Compressor       { get; }

    private readonly ISampleProvider _tail;

    public WaveFormat WaveFormat => _tail.WaveFormat;

    public EffectsChain(ISampleProvider source)
    {
        Filters         = new FilterBank(source);
        Eq              = new EqProcessor(Filters);
        NoiseSuppressor = new NoiseSuppressor(Eq);
        Gate            = new NoiseGate(NoiseSuppressor);
        Compressor      = new Compressor(Gate);
        _tail           = Compressor;
    }

    public int Read(float[] buffer, int offset, int count)
        => _tail.Read(buffer, offset, count);
}

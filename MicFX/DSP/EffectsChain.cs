using System.Threading;
using NAudio.Wave;

namespace MicFX.DSP;

/// <summary>
/// Chains InputGain → FilterBank → EqProcessor → NoiseSuppressor → NoiseGate → Compressor.
/// All processors are publicly accessible for parameter updates.
/// </summary>
public class EffectsChain : ISampleProvider
{
    public FilterBank       Filters          { get; }
    public EqProcessor      Eq               { get; }
    public NoiseSuppressor  NoiseSuppressor  { get; }
    public NoiseGate        Gate             { get; }
    public Compressor       Compressor       { get; }

    private readonly InputGainProvider _inputGain;
    private readonly ISampleProvider   _tail;

    public WaveFormat WaveFormat => _tail.WaveFormat;

    public EffectsChain(ISampleProvider source)
    {
        _inputGain      = new InputGainProvider(source);
        Filters         = new FilterBank(_inputGain);
        Eq              = new EqProcessor(Filters);
        NoiseSuppressor = new NoiseSuppressor(Eq);
        Gate            = new NoiseGate(NoiseSuppressor);
        Compressor      = new Compressor(Gate);
        _tail           = Compressor;
    }

    public void SetInputGainDb(float db) => _inputGain.SetGainDb(db);

    public int Read(float[] buffer, int offset, int count)
        => _tail.Read(buffer, offset, count);

    private sealed class InputGainProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private float _linearGain = 1f;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public InputGainProvider(ISampleProvider source) => _source = source;

        public void SetGainDb(float db)
            => Volatile.Write(ref _linearGain, MathF.Pow(10f, db / 20f));

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            float gain = Volatile.Read(ref _linearGain);
            if (gain <= 1f)
            {
                for (int i = offset; i < offset + read; i++)
                    buffer[i] *= gain;
            }
            else
            {
                for (int i = offset; i < offset + read; i++)
                    buffer[i] = MathF.Tanh(buffer[i] * gain);
            }
            return read;
        }
    }
}

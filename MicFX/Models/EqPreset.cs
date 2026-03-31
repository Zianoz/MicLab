namespace MicFX.Models;

public class EqPreset
{
    public string Name { get; set; } = "Default";
    public float[] EqGains { get; set; } = new float[10];
    public NoiseGateSettings NoiseGate { get; set; } = new();
    public CompressorSettings Compressor { get; set; } = new();
}

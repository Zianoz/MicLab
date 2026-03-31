namespace MicFX.Models;

public class AppSettings
{
    public string? InputDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }
    public string? MonitorDeviceId { get; set; }
    public float MonitorVolume { get; set; } = 0.8f;
    public bool MonitorMuted { get; set; } = true;
    public float[] EqGains { get; set; } = new float[10];
    public NoiseGateSettings NoiseGate { get; set; } = new();
    public CompressorSettings Compressor { get; set; } = new();
    public string? ActivePresetName { get; set; }
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 900;
    public double WindowHeight { get; set; } = 600;
}

public class NoiseGateSettings
{
    public float ThresholdDb { get; set; } = -40f;
    public float AttackMs { get; set; } = 10f;
    public float HoldMs { get; set; } = 100f;
    public float ReleaseMs { get; set; } = 100f;
}

public class CompressorSettings
{
    public float ThresholdDb { get; set; } = -18f;
    public float Ratio { get; set; } = 4f;
    public float AttackMs { get; set; } = 10f;
    public float ReleaseMs { get; set; } = 100f;
    public float MakeupGainDb { get; set; } = 0f;
}

namespace MicFX.Models;

public class AppSettings
{
    public string? InputDeviceId { get; set; }
    public string? InputDeviceName { get; set; }
    public string? OutputDeviceId { get; set; }
    public string? OutputDeviceName { get; set; }
    public string? MonitorDeviceId { get; set; }
    public string? MonitorDeviceName { get; set; }
    public float MonitorVolume { get; set; } = 0.8f;
    public bool MonitorMuted { get; set; } = false;
    public float InputGainDb { get; set; } = 0f;
    public bool  NoiseSuppressorEnabled  { get; set; } = true;
    public float NoiseSuppressorStrength { get; set; } = 0.45f;
    public float[] EqGains { get; set; } = new float[10];
    public FilterSettings Filters { get; set; } = new();
    public NoiseGateSettings NoiseGate { get; set; } = new();
    public CompressorSettings Compressor { get; set; } = new();
    public string? ActivePresetName { get; set; }
    public bool RunAtStartup { get; set; } = true;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 960;
    public double WindowHeight { get; set; } = 680;
}

public class FilterSettings
{
    public bool  HpfEnabled  { get; set; } = true;
    public float HpfCutoffHz { get; set; } = 80f;
    public bool  LpfEnabled  { get; set; } = false;
    public float LpfCutoffHz { get; set; } = 18000f;
}

public class NoiseGateSettings
{
    public bool Enabled { get; set; } = false;
    public float ThresholdDb { get; set; } = -40f;
    public float SpeechThreshold { get; set; } = 0.55f;
    public float CloseVoiceBias { get; set; } = 0.65f;
    public float FloorAttenuationDb { get; set; } = 18f;
    public float AttackMs { get; set; } = 8f;
    public float HoldMs { get; set; } = 140f;
    public float ReleaseMs { get; set; } = 180f;
}

public class CompressorSettings
{
    public bool Enabled { get; set; } = true;
    public float ThresholdDb { get; set; } = -18f;
    public float Ratio { get; set; } = 4f;
    public float AttackMs { get; set; } = 10f;
    public float ReleaseMs { get; set; } = 100f;
    public float MakeupGainDb { get; set; } = 0f;
}

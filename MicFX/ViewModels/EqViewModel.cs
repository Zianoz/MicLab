using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MicFX.Core;
using MicFX.DSP;

namespace MicFX.ViewModels;

public class EqBandViewModel : ObservableObject
{
    private readonly EqViewModel _parent;
    public int BandIndex { get; }
    public string FrequencyLabel { get; }

    private float _gainDb;
    public float GainDb
    {
        get => _gainDb;
        set
        {
            if (SetProperty(ref _gainDb, value))
                _parent.OnBandChanged(BandIndex, value);
        }
    }

    public string Description { get; }

    public EqBandViewModel(EqViewModel parent, int index)
    {
        _parent = parent;
        BandIndex = index;
        float freq = EqProcessor.BandFrequencies[index];
        FrequencyLabel = freq >= 1000 ? $"{freq / 1000:G}k" : $"{freq:G}";
        Description = index < BandDescriptions.Length ? BandDescriptions[index] : "";
    }

    private static readonly string[] BandDescriptions =
    [
        "31 Hz — Sub-bass. The deep rumble you feel more than hear (think movie explosions, kick drum thud). Rarely useful for voice — boosting here mostly adds mud. Cut if your mic sounds boomy.",
        "62 Hz — Bass. Chest resonance and low body. A small boost adds warmth and weight to voice. Too much = muddy, bassy mic sound. Cut if you're on a desk and picking up desk rumble.",
        "125 Hz — Low-mids. Fullness and warmth. This is where mic proximity effect lives — close-miked voices get very boomy here. Cut to clean up a overly warm or muddy sound.",
        "250 Hz — Low-mids. Body and presence. Too much causes a 'boxy' or 'talking-into-a-cardboard-tube' sound. A small cut here often makes budget mics sound more professional.",
        "500 Hz — Mids. The 'honky' or 'nasal' zone. Boosting can make voices sound like they're coming through a phone. Cutting slightly opens up clarity without thinning the sound too much.",
        "1k Hz — Upper-mids. Presence and clarity. Boosting adds edge and cuts through a mix. Too much sounds harsh and aggressive. This is where a lot of voice intelligibility lives.",
        "2k Hz — Upper-mids / presence. Sharpness and definition. Adds bite and articulation. Sensitive range — small boosts go a long way. Can cause ear fatigue if overdone.",
        "4k Hz — Presence. Consonant attack (S, T, K, P sounds). Boosting adds crispness and punch; boosting too much causes harsh sibilance. Cut here to tame a sharp or piercing mic.",
        "8k Hz — High presence / air. Crispness, breathiness, and 'brightness'. Adds sparkle and openness to voice. Too much = harsh and fizzy. Where cheap mics often peak unpleasantly.",
        "16k Hz — Air. The very top shimmer. Adds a sense of space and openness. Mostly subtle on voice. Boosting makes things sound airy and 'hi-fi'; cutting darkens and smooths the tone.",
    ];

}

public partial class EqViewModel : ObservableObject
{
    private AudioEngine? _engine;

    public ObservableCollection<EqBandViewModel> Bands { get; } = new();

    /// <summary>Raised whenever any band gain changes (for curve redraw).</summary>
    public event Action? BandsChanged;

    // ── Noise Suppressor ─────────────────────────────────────────────────────
    [ObservableProperty] private bool  _noiseSuppressorEnabled  = true;
    [ObservableProperty] private float _noiseSuppressorStrength = 0.45f;

    partial void OnNoiseSuppressorEnabledChanged(bool value)   => _engine?.NoiseSuppressor?.ApplyParams(value, NoiseSuppressorStrength);
    partial void OnNoiseSuppressorStrengthChanged(float value) => _engine?.NoiseSuppressor?.ApplyParams(NoiseSuppressorEnabled, value);

    // ── Noise Gate ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _gateEnabled = false;
    [ObservableProperty] private float _speechThreshold = 0.55f;
    [ObservableProperty] private float _closeVoiceBias = 0.65f;
    [ObservableProperty] private float _floorAttenuationDb = 18f;
    [ObservableProperty] private float _gateAttackMs = 8f;
    [ObservableProperty] private float _gateHoldMs = 140f;
    [ObservableProperty] private float _gateReleaseMs = 180f;

    partial void OnGateEnabledChanged(bool value)        => _engine?.Gate?.SetEnabled(value);
    partial void OnSpeechThresholdChanged(float value)   => ApplyGateParams();
    partial void OnCloseVoiceBiasChanged(float value)    => ApplyGateParams();
    partial void OnFloorAttenuationDbChanged(float value)=> ApplyGateParams();
    partial void OnGateAttackMsChanged(float value)      => ApplyGateParams();
    partial void OnGateHoldMsChanged(float value)        => ApplyGateParams();
    partial void OnGateReleaseMsChanged(float value)     => ApplyGateParams();

    // ── Filters (HPF / LPF) ──────────────────────────────────────────────────
    [ObservableProperty] private bool  _hpfEnabled  = true;
    [ObservableProperty] private float _hpfCutoffHz = 80f;
    [ObservableProperty] private bool  _lpfEnabled  = false;
    [ObservableProperty] private float _lpfCutoffHz = 18000f;

    partial void OnHpfEnabledChanged(bool value)   => _engine?.Filters?.ApplyHpf(value, HpfCutoffHz);
    partial void OnHpfCutoffHzChanged(float value) => _engine?.Filters?.ApplyHpf(HpfEnabled, value);
    partial void OnLpfEnabledChanged(bool value)   => _engine?.Filters?.ApplyLpf(value, LpfCutoffHz);
    partial void OnLpfCutoffHzChanged(float value) => _engine?.Filters?.ApplyLpf(LpfEnabled, value);

    // ── Compressor ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _compressorEnabled = true;
    [ObservableProperty] private float _compThresholdDb = -18f;
    [ObservableProperty] private float _compRatio = 4f;
    [ObservableProperty] private float _compAttackMs = 10f;
    [ObservableProperty] private float _compReleaseMs = 100f;
    [ObservableProperty] private float _compMakeupGainDb = 0f;

    partial void OnCompressorEnabledChanged(bool value) => _engine?.Compressor?.SetEnabled(value);
    partial void OnCompThresholdDbChanged(float value)  => _engine?.Compressor?.ApplyParams(value, CompRatio, CompAttackMs, CompReleaseMs, CompMakeupGainDb);
    partial void OnCompRatioChanged(float value)         => _engine?.Compressor?.ApplyParams(CompThresholdDb, value, CompAttackMs, CompReleaseMs, CompMakeupGainDb);
    partial void OnCompAttackMsChanged(float value)      => _engine?.Compressor?.ApplyParams(CompThresholdDb, CompRatio, value, CompReleaseMs, CompMakeupGainDb);
    partial void OnCompReleaseMsChanged(float value)     => _engine?.Compressor?.ApplyParams(CompThresholdDb, CompRatio, CompAttackMs, value, CompMakeupGainDb);
    partial void OnCompMakeupGainDbChanged(float value)  => _engine?.Compressor?.ApplyParams(CompThresholdDb, CompRatio, CompAttackMs, CompReleaseMs, value);

    // ─────────────────────────────────────────────────────────────────────────

    public EqViewModel()
    {
        for (int i = 0; i < EqProcessor.BandFrequencies.Length; i++)
            Bands.Add(new EqBandViewModel(this, i));
    }

    public void AttachEngine(AudioEngine engine)
    {
        _engine = engine;
        if (_engine.Eq != null)
            foreach (var band in Bands)
                _engine.Eq.SetBandGain(band.BandIndex, band.GainDb);

        _engine?.Filters?.ApplyHpf(HpfEnabled, HpfCutoffHz);
        _engine?.Filters?.ApplyLpf(LpfEnabled, LpfCutoffHz);
        _engine?.NoiseSuppressor?.ApplyParams(NoiseSuppressorEnabled, NoiseSuppressorStrength);
        _engine?.Gate?.SetEnabled(GateEnabled);
        ApplyGateParams();
        _engine?.Compressor?.SetEnabled(CompressorEnabled);
        _engine?.Compressor?.ApplyParams(CompThresholdDb, CompRatio, CompAttackMs, CompReleaseMs, CompMakeupGainDb);
    }

    internal void OnBandChanged(int bandIndex, float gainDb)
    {
        _engine?.Eq?.SetBandGain(bandIndex, gainDb);
        BandsChanged?.Invoke();
    }

    public float[] GetGains() => Bands.Select(b => b.GainDb).ToArray();

    public void SetGains(float[] gains)
    {
        for (int i = 0; i < gains.Length && i < Bands.Count; i++)
            Bands[i].GainDb = gains[i];
    }

    private void ApplyGateParams()
        => _engine?.Gate?.ApplyParams(SpeechThreshold, CloseVoiceBias, FloorAttenuationDb, GateAttackMs, GateHoldMs, GateReleaseMs);
}

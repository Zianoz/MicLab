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

    public EqBandViewModel(EqViewModel parent, int index)
    {
        _parent = parent;
        BandIndex = index;
        float freq = EqProcessor.BandFrequencies[index];
        FrequencyLabel = freq >= 1000 ? $"{freq / 1000:G}k" : $"{freq:G}";
    }
}

public partial class EqViewModel : ObservableObject
{
    private AudioEngine? _engine;

    public ObservableCollection<EqBandViewModel> Bands { get; } = new();

    /// <summary>Raised whenever any band gain changes (for curve redraw).</summary>
    public event Action? BandsChanged;

    // ── Noise Gate ────────────────────────────────────────────────────────────
    [ObservableProperty] private float _gateThresholdDb = -40f;
    [ObservableProperty] private float _gateAttackMs = 10f;
    [ObservableProperty] private float _gateHoldMs = 100f;
    [ObservableProperty] private float _gateReleaseMs = 100f;

    partial void OnGateThresholdDbChanged(float value) => _engine?.Gate?.ApplyParams(value, GateAttackMs, GateHoldMs, GateReleaseMs);
    partial void OnGateAttackMsChanged(float value)    => _engine?.Gate?.ApplyParams(GateThresholdDb, value, GateHoldMs, GateReleaseMs);
    partial void OnGateHoldMsChanged(float value)      => _engine?.Gate?.ApplyParams(GateThresholdDb, GateAttackMs, value, GateReleaseMs);
    partial void OnGateReleaseMsChanged(float value)   => _engine?.Gate?.ApplyParams(GateThresholdDb, GateAttackMs, GateHoldMs, value);

    // ── Compressor ────────────────────────────────────────────────────────────
    [ObservableProperty] private float _compThresholdDb = -18f;
    [ObservableProperty] private float _compRatio = 4f;
    [ObservableProperty] private float _compAttackMs = 10f;
    [ObservableProperty] private float _compReleaseMs = 100f;
    [ObservableProperty] private float _compMakeupGainDb = 0f;

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

        _engine?.Gate?.ApplyParams(GateThresholdDb, GateAttackMs, GateHoldMs, GateReleaseMs);
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
}

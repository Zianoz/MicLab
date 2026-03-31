using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MicFX.Core;

namespace MicFX.ViewModels;

public partial class MeterViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer;
    private AudioEngine? _engine;

    // Smoothed display values (0–1)
    [ObservableProperty] private double _inputRmsDisplay;
    [ObservableProperty] private double _outputRmsDisplay;
    [ObservableProperty] private double _inputPeakY; // pixel offset from bottom (inverted)

    private float _peakHold;
    private int _peakHoldFrames;
    private const int PeakHoldDuration = 30; // ~1 second at 30fps
    private const double Smoothing = 0.3;

    public MeterViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30fps
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void AttachEngine(AudioEngine engine) => _engine = engine;
    public void DetachEngine() => _engine = null;

    private void OnTick(object? sender, EventArgs e)
    {
        if (_engine == null || !_engine.IsRunning)
        {
            InputRmsDisplay = InputRmsDisplay * (1 - Smoothing); // decay to 0
            OutputRmsDisplay = OutputRmsDisplay * (1 - Smoothing);
            return;
        }

        float rms = _engine.InputRms;
        float peak = _engine.InputPeak;

        // Smooth RMS
        InputRmsDisplay = InputRmsDisplay + Smoothing * (rms - InputRmsDisplay);
        OutputRmsDisplay = InputRmsDisplay; // TODO Phase 5 refactor: separate output level

        // Peak hold
        if (peak > _peakHold)
        {
            _peakHold = peak;
            _peakHoldFrames = PeakHoldDuration;
        }
        else if (_peakHoldFrames > 0)
        {
            _peakHoldFrames--;
        }
        else
        {
            _peakHold = Math.Max(0f, _peakHold - 0.02f);
        }

        // Y position: 0 = top (loud), 100 = bottom (quiet)
        InputPeakY = -(double)_peakHold * 100;
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}

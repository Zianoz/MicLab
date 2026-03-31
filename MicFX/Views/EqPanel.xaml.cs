using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MicFX.DSP;
using MicFX.ViewModels;

namespace MicFX.Views;

public partial class EqPanel : System.Windows.Controls.UserControl
{
    public EqPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private EqViewModel? _vm;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
            _vm.BandsChanged -= UpdateCurve;

        _vm = DataContext as EqViewModel;
        if (_vm != null)
        {
            _vm.BandsChanged += UpdateCurve;
            UpdateCurve();
        }
    }

    private void UpdateCurve()
    {
        if (_vm == null) return;
        Dispatcher.Invoke(() => DrawCurve(_vm.GetGains()));
    }

    private void DrawCurve(float[] gains)
    {
        double width = EqCurveCanvas.ActualWidth;
        double height = EqCurveCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        // Simple linear interpolation between band gains for the display curve
        float[] freqs = EqProcessor.BandFrequencies;
        double minLog = Math.Log10(20);
        double maxLog = Math.Log10(20000);

        var points = new PointCollection();
        int steps = (int)width;

        for (int px = 0; px < steps; px++)
        {
            double logF = minLog + (maxLog - minLog) * px / (steps - 1);
            double f = Math.Pow(10, logF);

            // Interpolate gain: find the nearest bands
            double gain = InterpolateGain(f, freqs, gains);

            // Map gain (-12 to +12) to Y (0 = top = +12, height = bottom = -12)
            double y = height / 2 - (gain / 12.0) * (height / 2);
            points.Add(new System.Windows.Point(px, y));
        }

        EqCurveLine.Points = points;
    }

    private static double InterpolateGain(double freq, float[] freqs, float[] gains)
    {
        if (freq <= freqs[0]) return gains[0];
        if (freq >= freqs[^1]) return gains[^1];

        for (int i = 0; i < freqs.Length - 1; i++)
        {
            if (freq >= freqs[i] && freq <= freqs[i + 1])
            {
                double t = (Math.Log10(freq) - Math.Log10(freqs[i]))
                         / (Math.Log10(freqs[i + 1]) - Math.Log10(freqs[i]));
                return gains[i] + t * (gains[i + 1] - gains[i]);
            }
        }
        return 0;
    }
}

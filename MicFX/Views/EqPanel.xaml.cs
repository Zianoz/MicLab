using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MicFX.DSP;
using MicFX.ViewModels;
using Point = System.Windows.Point;
using Vector = System.Windows.Vector;

namespace MicFX.Views;

public partial class EqPanel : System.Windows.Controls.UserControl
{
    public EqPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => UpdateCurve();
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
        double w = EqCurveCanvas.ActualWidth;
        double h = EqCurveCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        float[] freqs = EqProcessor.BandFrequencies;
        int n = freqs.Length;

        double minLog = Math.Log10(20);
        double maxLog = Math.Log10(20000);
        double mid = h / 2;

        // Convert each band to a pixel-space knot point
        var pts = new Point[n];
        for (int i = 0; i < n; i++)
        {
            double px = (Math.Log10(freqs[i]) - minLog) / (maxLog - minLog) * w;
            double py = Math.Clamp(mid - (gains[i] / 12.0) * mid, 0, h);
            pts[i] = new Point(px, py);
        }

        // Catmull-Rom tangents (non-uniform chord-length weighting)
        var tan = new Vector[n];
        tan[0] = pts[1] - pts[0];
        tan[n - 1] = pts[n - 1] - pts[n - 2];
        for (int i = 1; i < n - 1; i++)
            tan[i] = (pts[i + 1] - pts[i - 1]) / 2;

        // Build smooth curve: flat lead-in → Bezier segments → flat lead-out
        var curveFigure = new PathFigure { StartPoint = new Point(0, pts[0].Y), IsClosed = false };
        curveFigure.Segments.Add(new LineSegment(pts[0], true));
        for (int i = 0; i < n - 1; i++)
        {
            var cp1 = pts[i]     + tan[i]     / 3;
            var cp2 = pts[i + 1] - tan[i + 1] / 3;
            curveFigure.Segments.Add(new BezierSegment(cp1, cp2, pts[i + 1], true));
        }
        curveFigure.Segments.Add(new LineSegment(new Point(w, pts[n - 1].Y), true));

        EqCurvePath.Data = new PathGeometry(new PathFigureCollection { curveFigure });

        // Fill: same curve closed back along the 0 dB centre line
        var fillFigure = new PathFigure { StartPoint = new Point(0, mid), IsClosed = true };
        fillFigure.Segments.Add(new LineSegment(new Point(0, pts[0].Y), false));
        fillFigure.Segments.Add(new LineSegment(pts[0], false));
        for (int i = 0; i < n - 1; i++)
        {
            var cp1 = pts[i]     + tan[i]     / 3;
            var cp2 = pts[i + 1] - tan[i + 1] / 3;
            fillFigure.Segments.Add(new BezierSegment(cp1, cp2, pts[i + 1], false));
        }
        fillFigure.Segments.Add(new LineSegment(new Point(w, pts[n - 1].Y), false));
        fillFigure.Segments.Add(new LineSegment(new Point(w, mid), false));

        EqCurveFill.Data = new PathGeometry(new PathFigureCollection { fillFigure });
    }
}

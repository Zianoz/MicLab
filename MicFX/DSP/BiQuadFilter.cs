namespace MicFX.DSP;

/// <summary>
/// Biquad filter using the Audio EQ Cookbook formulas (Robert Bristow-Johnson).
/// Direct Form II implementation. Thread-safe coefficient swap via volatile struct.
/// </summary>
public class BiQuadFilter
{
    public enum FilterType { LowPass, HighPass, BandPass, Notch, PeakingEQ, LowShelf, HighShelf }

    // Filter state (per-channel: stereo)
    private float _x1, _x2, _y1, _y2; // channel 0
    private float _x1r, _x2r, _y1r, _y2r; // channel 1

    // Coefficients — swapped atomically via _pending (boxed so volatile is valid on reference type)
    private Coeffs _active;
    private volatile object? _pending; // boxed Coeffs

    private struct Coeffs
    {
        public float A0, A1, A2, B0, B1, B2;
    }

    public BiQuadFilter()
    {
        // Identity (pass-through) by default
        _active = new Coeffs { B0 = 1f, A0 = 1f };
    }

    /// <summary>Apply gain-change atomically without rebuilding the filter object.</summary>
    public void SetPeakingEQ(float sampleRate, float centerFreq, float q, float gainDb)
    {
        _pending = (object)CalculatePeakingEQ(sampleRate, centerFreq, q, gainDb);
    }

    public void SetLowPass(float sampleRate, float cutoff, float q)
        => _pending = (object)Calculate(FilterType.LowPass, sampleRate, cutoff, q, 0f);

    public void SetHighPass(float sampleRate, float cutoff, float q)
        => _pending = (object)Calculate(FilterType.HighPass, sampleRate, cutoff, q, 0f);

    public void SetLowShelf(float sampleRate, float freq, float q, float gainDb)
        => _pending = (object)Calculate(FilterType.LowShelf, sampleRate, freq, q, gainDb);

    public void SetHighShelf(float sampleRate, float freq, float q, float gainDb)
        => _pending = (object)Calculate(FilterType.HighShelf, sampleRate, freq, q, gainDb);

    /// <summary>Process one sample (mono or a single channel of stereo). Call ch=0 for left, ch=1 for right.</summary>
    public float Transform(float input, int ch = 0)
    {
        // Atomically pick up any pending coefficient update
        var p = _pending;
        if (p != null)
        {
            _active = (Coeffs)p;
            _pending = null;
            // Reset state to avoid transient pop on hard parameter changes
            // (small pops acceptable; full zeroing is safest)
            _x1 = _x2 = _y1 = _y2 = 0f;
            _x1r = _x2r = _y1r = _y2r = 0f;
        }

        var c = _active;
        float output;

        if (ch == 0)
        {
            output = (c.B0 / c.A0) * input + (c.B1 / c.A0) * _x1 + (c.B2 / c.A0) * _x2
                     - (c.A1 / c.A0) * _y1 - (c.A2 / c.A0) * _y2;
            _x2 = _x1; _x1 = input;
            _y2 = _y1; _y1 = output;
        }
        else
        {
            output = (c.B0 / c.A0) * input + (c.B1 / c.A0) * _x1r + (c.B2 / c.A0) * _x2r
                     - (c.A1 / c.A0) * _y1r - (c.A2 / c.A0) * _y2r;
            _x2r = _x1r; _x1r = input;
            _y2r = _y1r; _y1r = output;
        }

        return output;
    }

    // ── Coefficient calculations (Audio EQ Cookbook) ──────────────────────────

    private static Coeffs CalculatePeakingEQ(float fs, float f0, float q, float gainDb)
    {
        double A = Math.Pow(10, gainDb / 40.0);
        double w0 = 2 * Math.PI * f0 / fs;
        double alpha = Math.Sin(w0) / (2 * q);

        return new Coeffs
        {
            B0 = (float)(1 + alpha * A),
            B1 = (float)(-2 * Math.Cos(w0)),
            B2 = (float)(1 - alpha * A),
            A0 = (float)(1 + alpha / A),
            A1 = (float)(-2 * Math.Cos(w0)),
            A2 = (float)(1 - alpha / A)
        };
    }

    private static Coeffs Calculate(FilterType type, float fs, float f0, float q, float gainDb)
    {
        double A = Math.Pow(10, gainDb / 40.0);
        double w0 = 2 * Math.PI * f0 / fs;
        double cosW = Math.Cos(w0);
        double sinW = Math.Sin(w0);
        double alpha = sinW / (2 * q);

        return type switch
        {
            FilterType.LowPass => new Coeffs
            {
                B0 = (float)((1 - cosW) / 2),
                B1 = (float)(1 - cosW),
                B2 = (float)((1 - cosW) / 2),
                A0 = (float)(1 + alpha),
                A1 = (float)(-2 * cosW),
                A2 = (float)(1 - alpha)
            },
            FilterType.HighPass => new Coeffs
            {
                B0 = (float)((1 + cosW) / 2),
                B1 = (float)(-(1 + cosW)),
                B2 = (float)((1 + cosW) / 2),
                A0 = (float)(1 + alpha),
                A1 = (float)(-2 * cosW),
                A2 = (float)(1 - alpha)
            },
            FilterType.LowShelf => new Coeffs
            {
                B0 = (float)(A * ((A + 1) - (A - 1) * cosW + 2 * Math.Sqrt(A) * alpha)),
                B1 = (float)(2 * A * ((A - 1) - (A + 1) * cosW)),
                B2 = (float)(A * ((A + 1) - (A - 1) * cosW - 2 * Math.Sqrt(A) * alpha)),
                A0 = (float)((A + 1) + (A - 1) * cosW + 2 * Math.Sqrt(A) * alpha),
                A1 = (float)(-2 * ((A - 1) + (A + 1) * cosW)),
                A2 = (float)((A + 1) + (A - 1) * cosW - 2 * Math.Sqrt(A) * alpha)
            },
            FilterType.HighShelf => new Coeffs
            {
                B0 = (float)(A * ((A + 1) + (A - 1) * cosW + 2 * Math.Sqrt(A) * alpha)),
                B1 = (float)(-2 * A * ((A - 1) + (A + 1) * cosW)),
                B2 = (float)(A * ((A + 1) + (A - 1) * cosW - 2 * Math.Sqrt(A) * alpha)),
                A0 = (float)((A + 1) - (A - 1) * cosW + 2 * Math.Sqrt(A) * alpha),
                A1 = (float)(2 * ((A - 1) - (A + 1) * cosW)),
                A2 = (float)((A + 1) - (A - 1) * cosW - 2 * Math.Sqrt(A) * alpha)
            },
            _ => new Coeffs { B0 = 1f, A0 = 1f } // pass-through fallback
        };
    }
}

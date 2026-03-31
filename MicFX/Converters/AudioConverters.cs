using System.Globalization;
using System.Windows.Data;

namespace MicFX.Converters;

/// <summary>Converts a float dB gain value to a display string like "+3.0 dB".</summary>
public class DbDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float f)
            return f >= 0 ? $"+{f:F1} dB" : $"{f:F1} dB";
        if (value is double d)
            return d >= 0 ? $"+{d:F1} dB" : $"{d:F1} dB";
        return "0.0 dB";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts a 0–1 float level to a bar height (multiplies by the converter parameter height).</summary>
public class LevelToHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double level = value is float f ? f : (value is double d ? d : 0.0);
        double maxHeight = parameter is string s && double.TryParse(s, out double h) ? h : 100.0;
        return Math.Max(0, Math.Min(maxHeight, level * maxHeight));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Inverts a bool (for mute toggle display).</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>Converts MonitorMuted bool to button label: true → "Unmute", false → "Mute".</summary>
public class MuteLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool muted && muted ? "Unmute" : "Mute";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

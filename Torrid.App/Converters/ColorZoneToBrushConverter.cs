namespace Torrid.App.Converters;

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Torrid.Core.Config;

/// <summary>
/// Maps a <see cref="ColorZone"/> to its <see cref="ColorPalette"/> brush. An optional numeric
/// ConverterParameter (e.g. "0.7") sets the brush opacity.
/// </summary>
public sealed class ColorZoneToBrushConverter : IValueConverter
{
    private static readonly Dictionary<(ColorZone, double), SolidColorBrush> Cache = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var zone = value is ColorZone z ? z : ColorZone.Blue;
        var opacity = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var o)
            ? Math.Clamp(o, 0, 1)
            : 1.0;

        lock (Cache)
        {
            if (!Cache.TryGetValue((zone, opacity), out var brush))
            {
                var (r, g, b) = ColorPalette.ForZone(zone);
                brush = new SolidColorBrush(Color.FromRgb(r, g, b)) { Opacity = opacity };
                brush.Freeze();
                Cache[(zone, opacity)] = brush;
            }

            return brush;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

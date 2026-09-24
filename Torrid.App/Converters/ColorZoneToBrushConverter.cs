namespace Torrid.App.Converters;

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Torrid.Core.Config;

public sealed class ColorZoneToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var zone = value is ColorZone z ? z : ColorZone.Blue;
        var (r, g, b) = ColorPalette.ForZone(zone);
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

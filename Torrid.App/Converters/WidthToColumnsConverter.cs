namespace Torrid.App.Converters;

using System.Globalization;
using System.Windows.Data;

/// <summary>Available width → column count: 2 when at least the ConverterParameter (default 640) wide, else 1.</summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var threshold = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : 640;
        return value is double width && width >= threshold ? 2 : 1;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

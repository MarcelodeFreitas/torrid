namespace Torrid.App.Converters;

using System.Globalization;
using System.Windows.Data;

/// <summary>Flips a bool both ways (used where a toggle shows the opposite of a flag, e.g. Cards while the list is off).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : false;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : false;
}

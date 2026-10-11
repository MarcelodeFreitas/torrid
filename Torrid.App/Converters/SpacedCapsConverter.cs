namespace Torrid.App.Converters;

using System.Globalization;
using System.Windows.Data;

/// <summary>Upper-cases text and adds a hair space between letters (WPF has no letter-spacing property).</summary>
public sealed class SpacedCapsConverter : IValueConverter
{
    private const char HairSpace = ' ';

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text ? string.Join(HairSpace, text.ToUpper(culture).AsEnumerable()) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

using System.Globalization;
using System.Windows.Data;

namespace OpenInventorExport.ConfigEditor;

/// <summary>
/// Shows a nullable enum (for example Rollup.Inseparable) in a drop-down of strings.
/// null is shown as "(mode default)", meaning "not set, use the default".
/// </summary>
public class NullableChoiceConverter : IValueConverter
{
    public const string DefaultLabel = "(mode default)";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() ?? DefaultLabel;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || text == DefaultLabel)
            return null;
        Type enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return Enum.Parse(enumType, text);
    }
}

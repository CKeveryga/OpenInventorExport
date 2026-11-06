using System.Globalization;
using System.Windows.Data;

namespace OpenInventorExport.ConfigEditor;

/// <summary>
/// Shows a nullable enum (for example Rollup.Inseparable or a table's Mode) in a drop-down of strings.
/// null ("not set, use the default") is shown as the ConverterParameter text, or "(mode default)" if none is given.
/// </summary>
public class NullableChoiceConverter : IValueConverter
{
    public const string DefaultLabel = "(mode default)";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() ?? Label(parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || text == Label(parameter))
            return null;
        Type enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return Enum.Parse(enumType, text);
    }

    private static string Label(object? parameter) => parameter as string ?? DefaultLabel;
}

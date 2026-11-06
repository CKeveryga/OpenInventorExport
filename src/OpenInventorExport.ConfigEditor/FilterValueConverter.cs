using System.Globalization;
using System.Windows.Data;
using OpenInventorExport.Core;

namespace OpenInventorExport.ConfigEditor;

/// <summary>
/// For a filter row (PropertyFilter): with no parameter, the Value text to show, or "(not used)" for IsEmpty and
/// IsNotEmpty; with the parameter "enabled", whether the Value can be edited.
/// </summary>
public class FilterValueConverter : IValueConverter
{
    public static bool UsesValue(FilterOperator op) => op is not (FilterOperator.IsEmpty or FilterOperator.IsNotEmpty);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PropertyFilter f) return parameter as string == "enabled" ? false : "";
        if (parameter as string == "enabled") return UsesValue(f.Operator);
        return UsesValue(f.Operator) ? f.Value ?? "" : "(not used)";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

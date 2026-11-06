using System.Globalization;
using System.Windows.Data;

namespace OpenInventorExport.ConfigEditor;

/// <summary>
/// Edits a list of strings (for example a property's AppliesTo) as one text: "Part, SheetMetal".
/// Unknown names are not rejected here; the problems list reports them.
/// An empty list is shown as the ConverterParameter text if one is given (e.g. "(all)").
/// </summary>
public class CommaListConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var items = value as IEnumerable<string> ?? Array.Empty<string>();
        string text = string.Join(", ", items);
        return text.Length == 0 && parameter is string empty ? empty : text;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}

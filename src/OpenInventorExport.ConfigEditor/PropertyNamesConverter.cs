using System.Globalization;
using System.Windows.Data;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.ConfigEditor;

/// <summary>
/// Turns a property set name into the property names to suggest for it: the Design Tracking names
/// for the Design Tracking set, nothing for User Defined (those names are your own, so you type them).
/// </summary>
public class PropertyNamesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value as string == PropertySetNames.DesignTracking ? Choices.DesignTrackingNames : Array.Empty<string>();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

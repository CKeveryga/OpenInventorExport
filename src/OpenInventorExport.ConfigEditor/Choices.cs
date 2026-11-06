using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;

namespace OpenInventorExport.ConfigEditor;

/// <summary>The fixed lists the drop-downs show. Used from XAML with x:Static.</summary>
public static class Choices
{
    /// <summary>The property sets the traverser reads (same list the config loader accepts).</summary>
    public static string[] PropertySets { get; } = { PropertySetNames.DesignTracking, PropertySetNames.UserDefined };

    /// <summary>
    /// The Design Tracking property names, as found in the robot fixture (samples/snapshots/robot-snapshot.json,
    /// collected with Inventor 2027). Offered as suggestions; any other name can still be typed.
    /// </summary>
    public static string[] DesignTrackingNames { get; } =
    {
        "Appearance", "Authority", "Catalog Web Link", "Categories", "Checked By", "Cost", "Cost Center",
        "Creation Time", "Date Checked", "Defer Updates", "Density", "Description", "Design Status", "Designer",
        "Document SubType", "Document SubType Name", "Engineer", "Engr Approved By", "Engr Date Approved",
        "External Property Revision Id", "Flat Pattern Area", "Flat Pattern Defer Update", "Flat Pattern Length",
        "Flat Pattern Width", "Language", "Last Updated With", "Manufacturer", "Mass", "Material",
        "Material Identifier", "Mfg Approved By", "Mfg Date Approved", "Parameterized Template", "Part Icon",
        "Part Number", "Part Property Revision Id", "Project", "Proxy Refresh Date", "Revit Category",
        "Sheet Metal Area", "Sheet Metal Length", "Sheet Metal Rule", "Sheet Metal Width", "Size Designation",
        "Standard", "Standard Revision", "Standards Organization", "Stock Number", "SurfaceArea", "Template Row",
        "User Status", "Valid MassProps", "Vendor", "Volume", "Weld Material"
    };

    public static UnresolvedPolicy[] UnresolvedPolicies { get; } = Enum.GetValues<UnresolvedPolicy>();
    public static RollupMode[] RollupModes { get; } = Enum.GetValues<RollupMode>();
    public static RollupKey[] RollupKeys { get; } = Enum.GetValues<RollupKey>();
    public static ExportScope[] Scopes { get; } = Enum.GetValues<ExportScope>();
    public static ContentCenterMode[] ContentCenterModes { get; } = Enum.GetValues<ContentCenterMode>();
    public static PropertyDataType[] DataTypes { get; } = Enum.GetValues<PropertyDataType>();

    public static ExportSource[] Sources { get; } = Enum.GetValues<ExportSource>();
    public static PdfSheetMode[] PdfSheetModes { get; } = Enum.GetValues<PdfSheetMode>();
}

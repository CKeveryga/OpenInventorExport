using System.Windows;
using System.Windows.Controls;
using OpenInventorExport.Core;

namespace OpenInventorExport.ConfigEditor;

/// <summary>
/// A row of check boxes, one per document type (Part, SheetMetal, ...), for a list such as a property's
/// AppliesTo or a task's DocumentTypes. Nothing checked = an empty list = all types.
/// It changes the bound list itself, so a one-way binding is enough.
/// A name that is not a known type (a typo in the file) is shown as an extra check box, so it can be unticked.
/// </summary>
public class DocumentTypePicker : WrapPanel
{
    public static readonly DependencyProperty TypesProperty = DependencyProperty.Register(
        nameof(Types), typeof(List<string>), typeof(DocumentTypePicker),
        new PropertyMetadata(null, (d, _) => ((DocumentTypePicker)d).Rebuild()));

    public List<string>? Types
    {
        get => (List<string>?)GetValue(TypesProperty);
        set => SetValue(TypesProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        var list = Types;
        if (list == null) return;

        var names = DocumentTypes.All
            .Concat(list.Where(t => !DocumentTypes.All.Contains(t, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        foreach (string name in names)
        {
            var box = new CheckBox
            {
                Content = name,
                Margin = new Thickness(0, 2, 10, 2),
                IsChecked = list.Contains(name, StringComparer.OrdinalIgnoreCase)
            };
            box.Checked += (_, _) =>
            {
                if (!list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
            };
            box.Unchecked += (_, _) => list.RemoveAll(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
            Children.Add(box);
        }
    }
}

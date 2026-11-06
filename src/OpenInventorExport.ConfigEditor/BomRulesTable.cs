using System.Windows;
using System.Windows.Controls;
using OpenInventorExport.Core;

namespace OpenInventorExport.ConfigEditor;

/// <summary>
/// Edits a BomStructures rule list as one fixed row per structure (Normal, Phantom, Reference, Inseparable,
/// Purchased), each with "the item itself" and "its children" drop-downs. The first entry of each drop-down is
/// "Default (...)" and says what the default is. Only rows that differ from Default are kept in the list,
/// so a structure can never be listed twice and an untouched row writes nothing to the file.
/// </summary>
public class BomRulesTable : Grid
{
    private static readonly BomStructureKind[] Order =
    {
        BomStructureKind.Normal, BomStructureKind.Phantom, BomStructureKind.Reference,
        BomStructureKind.Inseparable, BomStructureKind.Purchased
    };

    private readonly Dictionary<BomStructureKind, (ComboBox Item, ComboBox Children)> _rows = new();
    private List<BomStructureRule> _rules = new();
    private Func<BomStructureKind, string> _childrenDefault = _ => "kept";
    private bool _loading;

    /// <summary>
    /// One column per structure and two rows (the item itself, its children), so the table is wide and short.
    /// A drop-down's full text is also its tooltip, in case a column is too narrow to show it.
    /// </summary>
    public BomRulesTable()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var _ in Order)
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        for (int r = 0; r < 3; r++)
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Place(new TextBlock { Text = "The item itself", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }, 1, 0);
        Place(new TextBlock { Text = "Its children", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }, 2, 0);

        int column = 1;
        foreach (var kind in Order)
        {
            var item = new ComboBox { Margin = new Thickness(0, 2, 6, 2) };
            var children = new ComboBox { Margin = new Thickness(0, 2, 6, 2) };
            var k = kind;
            item.SelectionChanged += (_, _) => { ShowTextAsToolTip(item); if (!_loading) Write(k); };
            children.SelectionChanged += (_, _) => { ShowTextAsToolTip(children); if (!_loading) Write(k); };
            _rows[kind] = (item, children);

            Place(Header(kind.ToString()), 0, column);
            Place(item, 1, column);
            Place(children, 2, column);
            column++;
        }
    }

    private static void ShowTextAsToolTip(ComboBox box) =>
        box.ToolTip = (box.SelectedItem as ComboBoxItem)?.Content;

    /// <summary>
    /// Shows a rule list. <paramref name="childrenDefault"/> says what "Default" means for a structure's children
    /// (it differs between the BOM and a task), e.g. "kept" or "hidden in Flat".
    /// </summary>
    public void Load(List<BomStructureRule> rules, Func<BomStructureKind, string> childrenDefault)
    {
        _rules = rules;
        _childrenDefault = childrenDefault;
        _loading = true;
        try
        {
            foreach (var kind in Order)
            {
                var rule = rules.FirstOrDefault(r => r.Structure == kind);
                var (item, children) = _rows[kind];

                item.Items.Clear();
                item.Items.Add(new ComboBoxItem { Content = $"Default ({ItemDefault(kind)})", Tag = null });
                item.Items.Add(new ComboBoxItem { Content = "Included", Tag = true });
                item.Items.Add(new ComboBoxItem { Content = "Left out", Tag = false });
                item.SelectedIndex = rule?.Include switch { true => 1, false => 2, _ => 0 };

                children.Items.Clear();
                children.Items.Add(new ComboBoxItem { Content = $"Default ({childrenDefault(kind)})", Tag = null });
                children.Items.Add(new ComboBoxItem { Content = "Kept", Tag = ChildHandling.Include });
                children.Items.Add(new ComboBoxItem { Content = "Left out", Tag = ChildHandling.Exclude });
                children.SelectedIndex = rule?.Children switch { ChildHandling.Include => 1, ChildHandling.Exclude => 2, _ => 0 };
            }
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Re-reads what Default means for the children (e.g. after the rollup mode changed).</summary>
    public void UpdateLabels()
    {
        foreach (var kind in Order)
        {
            var box = _rows[kind].Children;
            if (box.Items.Count == 0 || box.Items[0] is not ComboBoxItem first) continue;
            string label = $"Default ({_childrenDefault(kind)})";
            if (Equals(first.Content, label)) continue;
            first.Content = label;
            if (box.SelectedIndex == 0)
            {
                // The closed drop-down shows a copy of the text, so select the entry again to show the new one.
                _loading = true;
                try { box.SelectedIndex = -1; box.SelectedIndex = 0; }
                finally { _loading = false; }
            }
        }
    }

    /// <summary>The item default, the same in the BOM and in tasks (verified against Inventor 2027).</summary>
    private static string ItemDefault(BomStructureKind kind) =>
        kind is BomStructureKind.Phantom or BomStructureKind.Reference ? "left out" : "included";

    private void Write(BomStructureKind kind)
    {
        var (itemBox, childrenBox) = _rows[kind];
        bool? include = (itemBox.SelectedItem as ComboBoxItem)?.Tag as bool?;
        var children = (childrenBox.SelectedItem as ComboBoxItem)?.Tag as ChildHandling?;

        var rule = _rules.FirstOrDefault(r => r.Structure == kind);
        if (include == null && children == null)
        {
            if (rule != null) _rules.Remove(rule);
            return;
        }
        if (rule == null)
        {
            rule = new BomStructureRule { Structure = kind };
            _rules.Add(rule);
        }
        rule.Include = include;
        rule.Children = children;
    }

    private static TextBlock Header(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 2) };

    private void Place(UIElement element, int row, int column)
    {
        SetRow(element, row);
        SetColumn(element, column);
        Children.Add(element);
    }
}

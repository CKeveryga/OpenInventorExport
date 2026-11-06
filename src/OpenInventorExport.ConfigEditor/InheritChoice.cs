using System.Windows.Controls;

namespace OpenInventorExport.ConfigEditor;

/// <summary>
/// Drives a drop-down for a setting that may be left unset (null) to inherit a value from elsewhere,
/// for example a table's Mode (from the General page) or Rollup.Inseparable (from the mode).
/// The first entry names what "not set" currently means, e.g. "From General (Structured)",
/// and its text is refreshed with UpdateLabel when the inherited value may have changed.
/// </summary>
public class InheritChoice<T> where T : struct, Enum
{
    private readonly ComboBox _box;
    private readonly Func<string> _inheritLabel;
    private Func<T?> _get = () => null;
    private Action<T?> _set = _ => { };
    private bool _loading;

    public InheritChoice(ComboBox box, Func<string> inheritLabel)
    {
        _box = box;
        _inheritLabel = inheritLabel;
        _box.SelectionChanged += (_, _) =>
        {
            if (_loading || _box.SelectedItem is not ComboBoxItem item) return;
            _set((T?)item.Tag);
        };
    }

    /// <summary>Points the drop-down at a setting (how to read and write it) and shows its value.</summary>
    public void Load(Func<T?> get, Action<T?> set)
    {
        _get = get;
        _set = set;
        _loading = true;
        try
        {
            _box.Items.Clear();
            _box.Items.Add(new ComboBoxItem { Content = _inheritLabel(), Tag = null });
            foreach (T value in Enum.GetValues<T>())
                _box.Items.Add(new ComboBoxItem { Content = value.ToString(), Tag = value });

            T? current = _get();
            _box.SelectedIndex = current == null ? 0 : Array.IndexOf(Enum.GetValues<T>(), current.Value) + 1;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Re-reads what "not set" means now (the selection is not changed).</summary>
    public void UpdateLabel()
    {
        if (_box.Items.Count > 0 && _box.Items[0] is ComboBoxItem first)
        {
            string label = _inheritLabel();
            if (Equals(first.Content, label)) return;
            first.Content = label;

            // The closed drop-down shows a copy of the selected text, so select the entry again to show the new text.
            if (_box.SelectedIndex == 0)
            {
                _loading = true;
                try { _box.SelectedIndex = -1; _box.SelectedIndex = 0; }
                finally { _loading = false; }
            }
        }
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenInventorExport.Core;

namespace OpenInventorExport.ConfigEditor;

/// <summary>
/// The Outputs part of the task page: a list with one line per output, and a form for the selected
/// output that shows only the settings its format uses. An output's format is chosen when it is added
/// and cannot be changed afterwards (Duplicate as another format makes a new one instead).
/// </summary>
public partial class MainWindow
{
    private readonly InheritChoice<RollupMode> _mode;
    private readonly InheritChoice<RollupKey> _key;

    private OutputSpec? SelectedOutput =>
        SelectedTask is { } task && OutputsList.SelectedIndex >= 0 && OutputsList.SelectedIndex < task.Outputs.Count
            ? task.Outputs[OutputsList.SelectedIndex]
            : null;

    // ---------- List ----------

    private void RebuildOutputsList(int selectIndex)
    {
        OutputsList.Items.Clear();
        if (SelectedTask is { } task)
            foreach (var o in task.Outputs)
                OutputsList.Items.Add(new ListBoxItem { Content = OutputLine(task, o) });
        OutputsList.SelectedIndex = Math.Min(selectIndex, OutputsList.Items.Count - 1);
        ShowOutputForm();
    }

    /// <summary>One line describing an output, e.g. "PDF   from drawing, all sheets  → PDF\".</summary>
    private static string OutputLine(ExportTask task, OutputSpec o)
    {
        string what = o.Format switch
        {
            ExportFormat.Csv or ExportFormat.Xlsx =>
                $"'{(string.IsNullOrWhiteSpace(o.Name) ? task.Name : o.Name)}', " +
                (o.Mode?.ToString() ?? "mode from General"),
            ExportFormat.Pdf => "from drawing, " + (o.PdfSheets == PdfSheetMode.AllInOne ? "all sheets" : "first sheet"),
            ExportFormat.Dwg or ExportFormat.Dxf => "from drawing" +
                (string.IsNullOrWhiteSpace(o.IniFile) ? "" : ", " + Path.GetFileName(o.IniFile)),
            _ => "from " + o.Source.ToString().ToLowerInvariant()
        };
        string where = string.IsNullOrWhiteSpace(o.Folder) ? "(root folder)" : o.Folder.Trim() + "\\";
        string warning = OutputRules.Leftovers(o).Count > 0 ? "  (!)" : "";
        return $"{o.Format.ToString().ToUpperInvariant(),-5} {what}  → {where}{warning}";
    }

    private void OnOutputSelected(object sender, SelectionChangedEventArgs e) => ShowOutputForm();

    // ---------- Form ----------

    private void ShowOutputForm()
    {
        var o = SelectedOutput;
        OutputForm.Visibility = o == null ? Visibility.Collapsed : Visibility.Visible;
        OutputForm.DataContext = o;
        if (o == null) return;

        bool table = OutputRules.IsTable(o.Format);
        OutputFormTitle.Text = table ? $"Selected: {o.Format} table" : $"Selected: {o.Format}";

        var required = OutputRules.RequiredSource(o.Format);
        SourceRow.Visibility = table ? Visibility.Collapsed : Visibility.Visible;
        SourceChoice.Visibility = required == null ? Visibility.Visible : Visibility.Collapsed;
        SourceFixed.Visibility = required == null ? Visibility.Collapsed : Visibility.Visible;
        SourceFixed.Text = required == null ? "" :
            $"{required} ({o.Format} always comes from the {required.ToString()!.ToLowerInvariant()})";

        SheetsRow.Visibility = OutputRules.UsesSheets(o.Format) ? Visibility.Visible : Visibility.Collapsed;
        IniRow.Visibility = OutputRules.UsesIniFile(o.Format) ? Visibility.Visible : Visibility.Collapsed;
        NameRow.Visibility = ModeRow.Visibility = KeyRow.Visibility = ColumnsRow.Visibility =
            table ? Visibility.Visible : Visibility.Collapsed;
        if (table)
        {
            _mode.Load(() => o.Mode, v => o.Mode = v);
            _key.Load(() => o.Key, v => o.Key = v);
        }
        RefreshOutputs();
    }

    /// <summary>Updates the list lines and the form's hints after any edit.</summary>
    private void RefreshOutputs()
    {
        if (SelectedTask is not { } task) return;

        for (int i = 0; i < task.Outputs.Count && i < OutputsList.Items.Count; i++)
            ((ListBoxItem)OutputsList.Items[i]).Content = OutputLine(task, task.Outputs[i]);

        var o = SelectedOutput;
        if (o == null) return;

        var leftovers = OutputRules.Leftovers(o);
        OutputFixPanel.Visibility = leftovers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OutputFixText.Text = leftovers.Count == 0 ? "" :
            $"This output has settings a {o.Format} cannot use: {string.Join("; ", leftovers)}.";

        // Same token rules as ExportPlanner ({type} stays as it is: it depends on each file).
        // A table leaves out the {type} levels of the root folder (it covers every document type).
        string template = task.FolderTemplate ?? "";
        if (OutputRules.IsTable(o.Format)) template = ExportPlanner.WithoutTypeLevels(template);
        string root = template
            .Replace("{task}", task.Name)
            .Replace("{format}", o.Format.ToString().ToLowerInvariant());
        string folder = (string.IsNullOrWhiteSpace(o.Folder) ? root : Path.Combine(root, o.Folder.Trim()))
            .Replace('/', '\\');
        FolderHint.Text = $"Written to: <output folder>\\{folder}\\" + (string.IsNullOrWhiteSpace(o.Folder) ? "  (empty = the task's root folder)" : "");

        NameHint.Text = string.IsNullOrWhiteSpace(o.Name) ? $"Empty = '{task.Name}' (the task name)" : "";
        _mode.UpdateLabel();
        _key.UpdateLabel();

        ColumnsNote.Text = o.Columns.Count > 0
            ? "Every table starts with File Name and Qty and ends with Status; these are the columns in between."
            : _config.BomColumns.Count > 0
                ? "None set here, so the General BOM columns are used: " + string.Join(", ", _config.BomColumns.Select(c => c.Name)) + "."
                : "None set here and none on the General page: the table has only File Name, Qty and Status.";
    }

    private void OnFixOutput(object sender, RoutedEventArgs e)
    {
        if (SelectedOutput is not { } o) return;
        OutputRules.Fix(o);
        ShowOutputForm();
        RefreshColumnsGrid();
        ScheduleCheck();
    }

    // ---------- Add, duplicate, remove, move ----------

    private void OnAddOutput(object sender, RoutedEventArgs e) =>
        ShowFormatMenu(AddOutputButton, format =>
            AddOutput(new OutputSpec { Format = format, Source = OutputRules.DefaultSource(format) }));

    /// <summary>
    /// Copies the selected output. "Same format" copies it exactly; another format keeps what that format
    /// can use (folder; for table to table also name, mode, key and columns) and drops the rest.
    /// </summary>
    private void OnDuplicateOutput(object sender, RoutedEventArgs e)
    {
        if (SelectedOutput is not { } original) return;
        ShowFormatMenu(DuplicateOutputButton, format =>
        {
            var copy = Clone(original);
            if (format != original.Format)
            {
                copy.Format = format;
                if (OutputRules.RequiredSource(original.Format) != null || OutputRules.IsTable(original.Format))
                    copy.Source = OutputRules.DefaultSource(format);
                OutputRules.Fix(copy);
            }
            AddOutput(copy);
        }, sameFormatFirst: original.Format);
    }

    private void ShowFormatMenu(Button button, Action<ExportFormat> pick, ExportFormat? sameFormatFirst = null)
    {
        var menu = new ContextMenu { PlacementTarget = button };
        if (sameFormatFirst is { } same)
        {
            var item = new MenuItem { Header = $"Same format ({same})" };
            item.Click += (_, _) => pick(same);
            menu.Items.Add(item);
            menu.Items.Add(new Separator());
        }
        foreach (var (format, label) in new[]
                 {
                     (ExportFormat.Step, "STEP (model)"), (ExportFormat.Pdf, "PDF (drawing)"),
                     (ExportFormat.Dwg, "DWG (drawing)"), (ExportFormat.Dxf, "DXF (drawing)"),
                     (ExportFormat.Dwf, "DWF (drawing or model)"), (ExportFormat.Csv, "CSV table"),
                     (ExportFormat.Xlsx, "XLSX table")
                 })
        {
            if (format == sameFormatFirst) continue;
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => pick(format);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void AddOutput(OutputSpec output)
    {
        if (SelectedTask is not { } task) return;
        int index = OutputsList.SelectedIndex < 0 ? task.Outputs.Count : OutputsList.SelectedIndex + 1;
        task.Outputs.Insert(index, output);
        RebuildOutputsList(selectIndex: index);
        ScheduleCheck();
    }

    private void OnRemoveOutput(object sender, RoutedEventArgs e)
    {
        if (SelectedTask is not { } task || SelectedOutput is not { } o) return;
        if (MessageBox.Show(this, $"Remove the {o.Format} output?", "Remove output",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        int index = OutputsList.SelectedIndex;
        task.Outputs.RemoveAt(index);
        RebuildOutputsList(selectIndex: Math.Max(0, index - 1));
        ScheduleCheck();
    }

    private void OnMoveOutputUp(object sender, RoutedEventArgs e) => MoveOutput(-1);
    private void OnMoveOutputDown(object sender, RoutedEventArgs e) => MoveOutput(+1);

    private void MoveOutput(int step)
    {
        if (SelectedTask is not { } task || SelectedOutput is not { } o) return;
        int from = OutputsList.SelectedIndex, to = from + step;
        if (to < 0 || to >= task.Outputs.Count) return;
        task.Outputs.RemoveAt(from);
        task.Outputs.Insert(to, o);
        RebuildOutputsList(selectIndex: to);
        ScheduleCheck();
    }

    // ---------- Ini file and columns ----------

    /// <summary>Picks an ini file; stored relative to the config's folder when it is inside it, else as a full path.</summary>
    private void OnBrowseIni(object sender, RoutedEventArgs e)
    {
        if (SelectedOutput is not { } o) return;
        string? configFolder = _path != null ? Path.GetDirectoryName(_path) : null;
        var dialog = new OpenFileDialog { Filter = "Ini files (*.ini)|*.ini|All files (*.*)|*.*", InitialDirectory = configFolder };
        if (dialog.ShowDialog(this) != true) return;

        string file = dialog.FileName;
        if (configFolder != null)
        {
            string relative = Path.GetRelativePath(configFolder, file);
            if (!relative.StartsWith("..") && !Path.IsPathRooted(relative))
                file = relative;
        }
        o.IniFile = file;
        ShowOutputForm();
        ScheduleCheck();
    }

    /// <summary>Offers the General BOM columns and every other table's columns; the chosen list replaces this table's.</summary>
    private void OnCopyColumns(object sender, RoutedEventArgs e)
    {
        if (SelectedOutput is not { } target) return;

        var sources = new List<(string Label, List<BomColumn> Columns)>();
        if (_config.BomColumns.Count > 0)
            sources.Add(($"General BOM columns ({_config.BomColumns.Count})", _config.BomColumns));
        foreach (var task in _config.Tasks)
            foreach (var o in task.Outputs.Where(o => o != target && OutputRules.IsTable(o.Format) && o.Columns.Count > 0))
                sources.Add(($"Task '{task.Name}': {o.Format} '{(string.IsNullOrWhiteSpace(o.Name) ? task.Name : o.Name)}' ({o.Columns.Count})", o.Columns));

        var menu = new ContextMenu { PlacementTarget = CopyColumnsButton };
        if (sources.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No other columns to copy yet", IsEnabled = false });
        foreach (var (label, columns) in sources)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) =>
            {
                // Added at the end; a column this table already has (same set and name) is not added twice.
                var missing = columns
                    .Where(c => !target.Columns.Any(t => SameColumn(t, c)))
                    .Select(c => new BomColumn { Set = c.Set, Name = c.Name })
                    .ToList();
                FinishEditing(ColumnsGrid);
                target.Columns.AddRange(missing);
                ColumnsGrid.Items.Refresh();
                ScheduleCheck();

                int skipped = columns.Count - missing.Count;
                if (skipped > 0)
                    MessageBox.Show(this,
                        $"{missing.Count} column(s) added. {skipped} were already in this table and were not added again.",
                        "Copy columns", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private static bool SameColumn(BomColumn a, BomColumn b) =>
        string.Equals(a.Set, b.Set, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Shows a column list that was changed in code.</summary>
    private void RefreshColumnsGrid()
    {
        FinishEditing(ColumnsGrid);
        ColumnsGrid.Items.Refresh();
    }

    /// <summary>Ends any edit in a grid; Items.Refresh throws while a row is being edited or added.</summary>
    private static void FinishEditing(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Row, true);
        grid.CancelEdit();
    }

    // ---------- Reordering column rows ----------

    private void OnTableColumnUp(object sender, RoutedEventArgs e) =>
        MoveRow(ColumnsGrid, SelectedOutput?.Columns, -1);
    private void OnTableColumnDown(object sender, RoutedEventArgs e) =>
        MoveRow(ColumnsGrid, SelectedOutput?.Columns, +1);
    private void OnBomColumnUp(object sender, RoutedEventArgs e) => MoveRow(BomColumnsGrid, _config.BomColumns, -1);
    private void OnBomColumnDown(object sender, RoutedEventArgs e) => MoveRow(BomColumnsGrid, _config.BomColumns, +1);

    /// <summary>Moves the selected grid row one place up (-1) or down (+1) in its list (columns, filters).</summary>
    private void MoveRow<T>(DataGrid grid, List<T>? list, int step) where T : class
    {
        FinishEditing(grid);
        if (list == null || grid.SelectedItem is not T item) return;
        int from = list.IndexOf(item), to = from + step;
        if (from < 0 || to < 0 || to >= list.Count) return;
        list.RemoveAt(from);
        list.Insert(to, item);
        grid.Items.Refresh();
        grid.SelectedItem = item;
        ScheduleCheck();
    }
}
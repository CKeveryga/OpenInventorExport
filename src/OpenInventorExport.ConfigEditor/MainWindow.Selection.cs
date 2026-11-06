using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OpenInventorExport.Core;

namespace OpenInventorExport.ConfigEditor;

/// <summary>Filters and BOM structure rules: the task's selection and the General page's Rollup rules.</summary>
public partial class MainWindow
{
    /// <summary>
    /// What "Default" means for a structure's children in a task: Reference children are left out, everything
    /// else is kept. Unlike the BOM, a Purchased or Inseparable assembly's children stay selected for export
    /// (CLAUDE.md 4.6), and task tables list them too (TaskTable forces IncludeChildren).
    /// </summary>
    private static string TaskChildrenDefault(BomStructureKind kind) =>
        kind == BomStructureKind.Reference ? "left out" : "kept";

    /// <summary>
    /// What "Default" means for a structure's children in the bom command. Inseparable and Purchased follow
    /// Rollup.Inseparable, or the mode when that is not set (Rollup.Apply: Flat hides them, other modes keep them).
    /// </summary>
    private string RollupChildrenDefault(BomStructureKind kind)
    {
        switch (kind)
        {
            case BomStructureKind.Reference:
                return "left out";
            case BomStructureKind.Inseparable:
            case BomStructureKind.Purchased:
                var rollup = _config.Rollup;
                var resolved = rollup.Inseparable ??
                               (rollup.Mode == RollupMode.Flat ? InseparableHandling.AsSingleItem : InseparableHandling.IncludeChildren);
                string what = resolved == InseparableHandling.AsSingleItem ? "hidden, single item" : "kept";
                string why = rollup.Inseparable != null ? "Inseparable setting" : $"{rollup.Mode} mode";
                return $"{what}: {why}";
            default:
                return "kept";
        }
    }

    private static string FiltersHelp =>
        "Equals, NotEquals and Contains ignore case. IsEmpty and IsNotEmpty need no value (a missing property counts as empty). " +
        $"LessThan, GreaterThan and the other number tests need a number, optionally with a unit ({Units.SupportedList}), e.g. 200 mm or 0.25 lb.";

    /// <summary>
    /// After the Operator of a row changes: a value is cleared when the new operator does not use one, and the
    /// grid is redrawn so the Value cell shows "(not used)" or the value again.
    /// </summary>
    private void OnFilterCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Column.Header as string != "Operator") return;
        var row = e.Row.Item as PropertyFilter;
        // Background priority: after the new operator has been written to the row.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (row != null && !FilterValueConverter.UsesValue(row.Operator))
                row.Value = null;
            FinishEditing(FiltersGrid);
            FiltersGrid.Items.Refresh();
            ScheduleCheck();
        }));
    }

    private void OnFilterUp(object sender, RoutedEventArgs e) => MoveRow(FiltersGrid, SelectedTask?.Selection.Filters, -1);
    private void OnFilterDown(object sender, RoutedEventArgs e) => MoveRow(FiltersGrid, SelectedTask?.Selection.Filters, +1);
}

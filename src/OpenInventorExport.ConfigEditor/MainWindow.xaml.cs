using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenInventorExport.Core;

namespace OpenInventorExport.ConfigEditor;

/// <summary>
/// The editor window. It edits the Core config classes directly (data binding) and re-checks the
/// whole config after every change, showing the problems at the bottom.
///
/// Files are opened with ExportConfigJson.Parse (paths stay as written, problems are allowed) and
/// written with ExportConfigFile.Save (refuses while there are problems, keeps a .bak).
/// Never ExportConfigFile.Load here: it makes ini paths absolute.
/// </summary>
public partial class MainWindow : Window
{
    private ExportConfig _config = new();
    private string? _path;          // null = never saved
    private string _savedJson = ""; // the config as last opened or saved, to tell whether it changed
    private bool _checkPending;

    public MainWindow()
    {
        InitializeComponent();

        // Any edit anywhere in the window schedules a re-check. The key and mouse events catch
        // grid edits (commit with Enter, Delete a row), which have no routed "changed" event.
        AddHandler(TextBoxBase.TextChangedEvent, new RoutedEventHandler((_, _) => ScheduleCheck()));
        AddHandler(Selector.SelectionChangedEvent, new RoutedEventHandler((_, _) => ScheduleCheck()));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => ScheduleCheck()));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => ScheduleCheck()));
        AddHandler(PreviewKeyUpEvent, new KeyEventHandler((_, _) => ScheduleCheck()));
        AddHandler(PreviewMouseUpEvent, new MouseButtonEventHandler((_, _) => ScheduleCheck()));

        ShowConfig(NewConfig(), path: null);
    }

    private bool IsChanged => ExportConfigJson.ToJson(_config) != _savedJson;

    // ---------- Open / save ----------

    /// <summary>Opens a config file. A file with problems still opens; the problems are listed.</summary>
    public void OpenFile(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            var config = ExportConfigJson.Parse(File.ReadAllText(fullPath));
            ShowConfig(config, fullPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this,
                "This file cannot be opened in the editor:\n\n" + ex.Message +
                "\n\nA misspelled name or a JSON syntax error must be fixed in a text editor first.",
                "Open config", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowConfig(ExportConfig config, string? path)
    {
        _config = config;
        _path = path;
        _savedJson = ExportConfigJson.ToJson(config);
        GeneralPage.DataContext = _config;
        RebuildPageList(selectIndex: 0);
        Refresh();
    }

    private bool SaveTo(string path)
    {
        try
        {
            ExportConfigFile.Save(_config, path);
        }
        catch (InvalidDataException)
        {
            MessageBox.Show(this, "Not saved. Fix the problems listed at the bottom of the window first.",
                "Save config", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Not saved:\n\n" + ex.Message, "Save config", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        _path = Path.GetFullPath(path);
        _savedJson = ExportConfigJson.ToJson(_config);
        Refresh();
        return true;
    }

    /// <summary>True when there are no unsaved changes, or the user agrees to lose them.</summary>
    private bool MayDiscardChanges() =>
        !IsChanged ||
        MessageBox.Show(this, "The config has unsaved changes. Discard them?", "Config Editor",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (MayDiscardChanges())
            ShowConfig(NewConfig(), path: null);
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (!MayDiscardChanges()) return;
        var dialog = new OpenFileDialog { Filter = "Config files (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
            OpenFile(dialog.FileName);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_path == null)
            OnSaveAs(sender, e);
        else
            SaveTo(_path);
    }

    private void OnSaveAs(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Config files (*.json)|*.json",
            FileName = _path != null ? Path.GetFileName(_path) : "export.config.json",
            InitialDirectory = _path != null ? Path.GetDirectoryName(_path) : null
        };
        if (dialog.ShowDialog(this) == true)
            SaveTo(dialog.FileName);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!MayDiscardChanges())
            e.Cancel = true;
    }

    // ---------- Task list ----------

    private void RebuildPageList(int selectIndex)
    {
        PageList.Items.Clear();
        PageList.Items.Add(new ListBoxItem { Content = "General", FontWeight = FontWeights.Bold });
        foreach (var task in _config.Tasks)
            PageList.Items.Add(new ListBoxItem { Content = TaskLabel(task) });
        PageList.SelectedIndex = Math.Min(selectIndex, PageList.Items.Count - 1);
    }

    private static string TaskLabel(ExportTask task) =>
        "Task: " + (string.IsNullOrWhiteSpace(task.Name) ? "(no name)" : task.Name);

    private ExportTask? SelectedTask =>
        PageList.SelectedIndex >= 1 ? _config.Tasks[PageList.SelectedIndex - 1] : null;

    private void OnPageSelected(object sender, SelectionChangedEventArgs e)
    {
        var task = SelectedTask;
        GeneralPage.Visibility = task == null ? Visibility.Visible : Visibility.Collapsed;
        TaskPage.Visibility = task == null ? Visibility.Collapsed : Visibility.Visible;
        TaskPage.DataContext = task;
        TaskSummary.Text = task == null ? "" : Summary(task);
    }

    private void OnAddTask(object sender, RoutedEventArgs e)
    {
        int n = _config.Tasks.Count + 1;
        while (_config.Tasks.Any(t => string.Equals(t.Name, "Task" + n, StringComparison.OrdinalIgnoreCase)))
            n++;

        _config.Tasks.Add(NewTask("Task" + n));
        RebuildPageList(selectIndex: _config.Tasks.Count);
    }

    /// <summary>
    /// A new task starts with one Xlsx table (a BOM of everything the task selects, named after the task),
    /// so it is valid at once. A task needs at least one output.
    /// </summary>
    private static ExportTask NewTask(string name) => new ExportTask
    {
        Name = name,
        Outputs = { new OutputSpec { Format = ExportFormat.Xlsx } }
    };

    /// <summary>A new config: one task "BOM" with an Xlsx table, so it can be saved straight away.</summary>
    private static ExportConfig NewConfig() => new ExportConfig { Tasks = { NewTask("BOM") } };

    private void OnRemoveTask(object sender, RoutedEventArgs e)
    {
        var task = SelectedTask;
        if (task == null) return;
        if (MessageBox.Show(this, $"Remove the task '{task.Name}'?", "Remove task",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        int index = PageList.SelectedIndex;
        _config.Tasks.Remove(task);
        RebuildPageList(selectIndex: index - 1);
    }

    // ---------- Checking ----------

    private void ScheduleCheck()
    {
        if (_checkPending) return;
        _checkPending = true;
        // Background priority: runs after the edit has reached the config object.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _checkPending = false;
            Refresh();
        }));
    }

    /// <summary>Re-checks the config and updates the problems list, task names and title.</summary>
    private void Refresh()
    {
        var problems = _path != null
            ? ExportConfigFile.Problems(_config, _path)
            : ExportConfigJson.Validate(_config);

        ProblemsList.ItemsSource = problems;
        ProblemsHeading.Text = problems.Count == 0
            ? "No problems."
            : $"Problems ({problems.Count}): the config cannot be saved until these are fixed.";
        if (_path == null)
            ProblemsHeading.Text += "  (ini files are checked once the config has been saved.)";

        for (int i = 0; i < _config.Tasks.Count && i + 1 < PageList.Items.Count; i++)
            ((ListBoxItem)PageList.Items[i + 1]).Content = TaskLabel(_config.Tasks[i]);

        if (SelectedTask is { } task)
            TaskSummary.Text = Summary(task);

        Title = "Config Editor - " + (_path ?? "(new config)") + (IsChanged ? " *" : "");
    }

    /// <summary>A read-only description of the parts of a task that cannot be edited yet.</summary>
    private static string Summary(ExportTask task)
    {
        var s = task.Selection;
        var text = new StringBuilder();
        if (s.Filters.Count == 0 && s.BomStructures.Count == 0)
            text.AppendLine("No filters, no BOM structure rules (defaults apply).");
        foreach (var f in s.Filters)
            text.AppendLine($"Filter: {f.Property} {f.Operator} {f.Value}");
        foreach (var r in s.BomStructures)
            text.AppendLine($"BOM rule: {r.Structure} item {r.Include?.ToString() ?? "default"}, children {r.Children?.ToString() ?? "default"}");
        text.AppendLine();
        text.AppendLine("Outputs:");
        foreach (var o in task.Outputs)
        {
            string where = string.IsNullOrEmpty(o.Folder) ? "root folder" : $"folder '{o.Folder}'";
            string what = o.Format is ExportFormat.Csv or ExportFormat.Xlsx
                ? $"{o.Format} table '{o.Name ?? task.Name}'" + (o.Mode != null ? $" ({o.Mode})" : "")
                : $"{o.Format} from {o.Source}";
            text.AppendLine($"  {what}, {where}");
        }
        return text.ToString();
    }
}

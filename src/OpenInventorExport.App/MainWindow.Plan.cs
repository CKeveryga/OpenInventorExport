using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using OpenInventorExport.Core;
using OpenInventorExport.Inventor;

namespace OpenInventorExport.App;

/// <summary>One line of the plan grid: a file to export or a table to write. During a run, State, Detail and Time change to the result.</summary>
public sealed class PlanRow : INotifyPropertyChanged
{
    private string _state = "";
    private string _detail = "";
    private string _time = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string State { get => _state; set { _state = value; Changed(nameof(State)); } }
    /// <summary>Seconds the export took, filled in during a run.</summary>
    public string Time { get => _time; set { _time = value; Changed(nameof(Time)); } }
    public string Task { get; set; } = "";
    public string Format { get; set; } = "";
    public string Source { get; set; } = "";
    public string File { get; set; } = "";
    /// <summary>The model file's Design Tracking Description (empty for tables or when not set).</summary>
    public string Description { get; set; } = "";
    /// <summary>Output path, shown relative to the output folder.</summary>
    public string Output { get; set; } = "";
    public string Detail { get => _detail; set { _detail = value; Changed(nameof(Detail)); } }

    /// <summary>Text the filter box searches.</summary>
    public string SearchText => $"{State} {Task} {Format} {Source} {File} {Description} {Output} {Detail}";

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Step 4: Plan, and the Plan / Left out by rules / Unreadable tabs.</summary>
public partial class MainWindow
{
    private PlanResult? _plan;
    private ICollectionView? _planView;

    /// <summary>
    /// After the first Plan, the window re-plans by itself whenever the task, config, output folder or
    /// snapshot changes, so the grid always shows what Run would do. Off until Plan is pressed once.
    /// </summary>
    private bool _autoPlan;
    private bool _planQueued;
    private bool _planning;
    private bool _planAgainAfterThis;

    private async void OnPlan(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            MessageBox.Show(this, "Choose an output folder first.", "Plan", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (await RunPlan(automatic: false))
        {
            _autoPlan = true;
            Tabs.SelectedItem = PlanTab;
        }
    }

    /// <summary>
    /// Plans with the current inputs and shows the result. An automatic plan reports a failure in the Plan tab
    /// instead of a message box. Returns true when a plan is shown.
    /// </summary>
    private async Task<bool> RunPlan(bool automatic)
    {
        if (_snapshot == null || _config == null || string.IsNullOrWhiteSpace(OutputFolder)) return false;
        if (_planning)
        {
            _planAgainAfterThis = true;   // inputs changed while planning: plan once more when this one ends
            return false;
        }

        var snapshot = _snapshot;
        var config = _config;
        string outRoot = OutputFolder;
        string? only = SelectedTask;

        _planning = true;
        SetBusy(true, automatic ? "Planning again (something changed)..." : "Planning...");
        try
        {
            // Off the window's thread: with drawing jobs it asks Inventor for the active project.
            var plan = await Task.Run(() => ExportWorkflow.Plan(snapshot, config, outRoot, only));
            ShowPlan(plan, outRoot);
            SetStatus(automatic ? "Planned again with the new settings. Nothing has been exported."
                                : "Planned. Nothing has been exported.");
            return true;
        }
        catch (Exception ex)
        {
            if (automatic)
            {
                PlanInfo.Foreground = System.Windows.Media.Brushes.DarkRed;
                PlanInfo.Text = "Could not plan: " + ex.Message;
            }
            else
            {
                MessageBox.Show(this, ex.Message, "Plan", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            SetStatus("Plan failed.");
            return false;
        }
        finally
        {
            _planning = false;
            SetBusy(false);
            if (_planAgainAfterThis)
            {
                _planAgainAfterThis = false;
                RequestReplan();
            }
        }
    }

    /// <summary>
    /// Asks for one automatic re-plan, run once the window is idle. Several changes in a row
    /// (e.g. loading a config also refills the task list) give a single plan.
    /// </summary>
    private void RequestReplan()
    {
        if (!_autoPlan || _planQueued) return;
        _planQueued = true;
        Dispatcher.BeginInvoke(async () =>
        {
            _planQueued = false;
            await RunPlan(automatic: true);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ShowPlan(PlanResult plan, string outRoot)
    {
        _plan = plan;
        var descriptions = DescriptionsByFile(_snapshot);

        var rows = plan.Jobs.Select(j => new PlanRow
        {
            State = j.State.ToString(),
            Task = j.TaskName,
            Format = j.Output.Format.ToString(),
            Source = j.Output.Source.ToString(),
            File = Path.GetFileName(PathUtil.StripMemberSuffix(j.ModelPath)),
            Description = descriptions.TryGetValue(PathUtil.StripMemberSuffix(j.ModelPath), out var d) ? d : "",
            Output = Relative(outRoot, j.OutputPath),
            Detail = j.Detail ?? "",
        }).Concat(plan.Tables.Select(t => new PlanRow
        {
            State = t.State.ToString(),
            Task = t.TaskName,
            Format = t.Output.Format.ToString(),
            Source = "(table)",
            File = "(every file the task selects)",
            Output = Relative(outRoot, t.OutputPath),
            Detail = t.Detail ?? "",
        })).ToList();

        _rows = rows;
        RunSummary.Visibility = Visibility.Collapsed;
        _planView = CollectionViewSource.GetDefaultView(rows);
        _planView.Filter = PlanFilterMatches;
        PlanGrid.ItemsSource = _planView;

        int leftOutFiles = plan.Exclusions.Count;
        PlanInfo.Foreground = System.Windows.Media.Brushes.Black;
        PlanInfo.Text =
            $"Task: {SelectedTask ?? "all tasks"}.  Planned files: {plan.PlannedCount}.  Conflicts: {plan.ConflictCount}.  " +
            $"Tables: {plan.Tables.Count}.  Left out by rules: {leftOutFiles}.  Output folder: {outRoot}" +
            Environment.NewLine + "Nothing has been exported yet.";

        var warnings = new List<string>();
        if (plan.Unreadable.Count > 0)
            warnings.Add(plan.Policy == UnresolvedPolicy.Block
                ? $"{plan.Unreadable.Count} component(s) could not be read. Exports will refuse until they are resolved (Unresolved = Block)."
                : $"{plan.Unreadable.Count} component(s) could not be read. Exports will go ahead and log each one (Unresolved = Warn).");
        if (plan.ConflictCount > 0)
            warnings.Add($"{plan.ConflictCount} output(s) would be written twice; the second one is marked Conflict and skipped.");
        if (plan.ProjectWarning != null)
            warnings.Add(plan.ProjectWarning);
        if (plan.ContentCenterNotRecorded > 0)
            warnings.Add($"A Content Center rule is active, but {plan.ContentCenterNotRecorded} part(s) have no Content Center flag. " +
                         "They are treated as NOT Content Center. Collect again to be sure.");
        PlanWarnings.Text = string.Join(Environment.NewLine, warnings);
        PlanWarnings.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        LeftOutGrid.ItemsSource = plan.Exclusions;
        LeftOutInfo.Text = plan.Exclusions.Count == 0
            ? "The rules left nothing out."
            : "Left out by rules: " + string.Join("   ·   ", ExclusionReport.Summary(plan.Exclusions)) +
              Environment.NewLine + "Each file is listed once per task, with the first rule that left it out.";

        UnreadableGrid.ItemsSource = plan.Unreadable;
        UnreadableInfo.Text = plan.Unreadable.Count == 0
            ? "Every component could be read."
            : $"{plan.Unreadable.Count} component(s) could not be read. An unresolved assembly's children cannot be seen at all, so more may be missing.";

        PlanTab.Header = $"Plan ({rows.Count})";
        LeftOutTab.Header = $"Left out by rules ({plan.Exclusions.Count})";
        UnreadableTab.Header = $"Unreadable ({plan.Unreadable.Count})";
    }

    /// <summary>
    /// Forgets the plan when anything it was made from changes (config, task, output folder, snapshot),
    /// so a stale plan can never be shown as current, then re-plans if Plan has been pressed before.
    /// </summary>
    private void ClearPlan()
    {
        ClearPlanDisplay();
        RequestReplan();
    }

    private void ClearPlanDisplay()
    {
        if (PlanGrid == null) return;   // during InitializeComponent
        bool hadPlan = _plan != null;
        _plan = null;
        _rows = null;
        _planView = null;
        RunSummary.Visibility = Visibility.Collapsed;
        PlanGrid.ItemsSource = null;
        LeftOutGrid.ItemsSource = null;
        UnreadableGrid.ItemsSource = null;
        PlanWarnings.Visibility = Visibility.Collapsed;
        PlanInfo.Foreground = System.Windows.Media.Brushes.Gray;
        PlanInfo.Text = _autoPlan && _snapshot != null && _config != null
            ? "Something changed (config, task, output folder or snapshot). Planning again..."
            : hadPlan
                ? "Something changed (config, task, output folder or snapshot). Press Plan again."
                : "Collect or load a snapshot, then press Plan.";
        LeftOutInfo.Text = "What each task's rules leave out, and why. Shown after Plan.";
        UnreadableInfo.Text = "Components Inventor could not read. Shown after Plan.";
        PlanTab.Header = "Plan";
        LeftOutTab.Header = "Left out by rules";
        UnreadableTab.Header = "Unreadable";
        UpdateButtons();
    }

    /// <summary>Plan needs a snapshot and a loaded config; Run needs a current plan that is not blocked.</summary>
    private void UpdateButtons()
    {
        if (PlanButton == null || RunButton == null) return;
        bool idle = CollectButton.IsEnabled;
        PlanButton.IsEnabled = _snapshot != null && _config != null && idle;
        RunButton.IsEnabled = _plan != null && !_plan.Blocked && idle;
        RunButton.ToolTip = _plan == null
            ? "Press Plan first: Run exports exactly what the plan shows."
            : _plan.Blocked
                ? $"Blocked: {_plan.Unreadable.Count} component(s) could not be read and the config says Unresolved = Block. " +
                  "Resolve the files in Inventor and collect again (see the Unreadable tab)."
                : "Exports what the plan shows. Asks first.";
    }

    private void OnPlanFilterChanged(object sender, TextChangedEventArgs e) => _planView?.Refresh();

    private bool PlanFilterMatches(object item)
    {
        string text = PlanFilter.Text.Trim();
        return text.Length == 0 || (item is PlanRow r && r.SearchText.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Model file (without model-state suffix) -> its Design Tracking Description, from the first occurrence that has one.</summary>
    private static Dictionary<string, string> DescriptionsByFile(Core.Model.Snapshot? snapshot)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in snapshot?.Components ?? new List<Core.Model.ComponentRecord>())
        {
            if (c.FullPath == null) continue;
            string file = PathUtil.StripMemberSuffix(c.FullPath);
            string? value = c.FindProperty(Core.Model.PropertySetNames.DesignTracking, "Description")?.Value;
            if (!string.IsNullOrWhiteSpace(value) && !map.ContainsKey(file))
                map[file] = value!;
        }
        return map;
    }

    private static string Relative(string root, string path)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }
}

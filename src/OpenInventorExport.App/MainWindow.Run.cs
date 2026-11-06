using System.Text;
using System.Windows;
using System.Windows.Media;
using OpenInventorExport.Core;
using OpenInventorExport.Inventor;

namespace OpenInventorExport.App;

/// <summary>Step 5: run the plan, show each result as it happens, and the outcome per task at the end.</summary>
public partial class MainWindow
{
    private List<PlanRow>? _rows;
    private CancellationTokenSource? _runCancel;

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        if (_plan == null || _rows == null || _snapshot == null || _config == null) return;
        var plan = _plan;
        var rows = _rows;
        var snapshot = _snapshot;
        var config = _config;
        string outRoot = OutputFolder;

        string unreadableNote = plan.Unreadable.Count > 0
            ? $"{Environment.NewLine}{Environment.NewLine}{plan.Unreadable.Count} component(s) could not be read; they are logged as Unreadable (Unresolved = Warn)."
            : "";
        string question =
            $"Export {plan.PlannedCount} file(s) and {plan.Tables.Count} table(s) for {SelectedTask ?? "all tasks"}?" +
            $"{Environment.NewLine}{Environment.NewLine}Into: {outRoot}{Environment.NewLine}" +
            "Existing files with the same names are overwritten. Inventor will be busy until it finishes." + unreadableNote;
        if (MessageBox.Show(this, question, "Run exports", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        // Rows are in plan order: file jobs first, then tables. Results arrive in the same order.
        int jobCount = plan.Jobs.Count;
        int nextJob = 0, nextTable = 0, done = 0;
        int toRun = plan.PlannedCount;

        _runCancel = new CancellationTokenSource();
        var token = _runCancel.Token;
        PlanFilter.Text = "";
        Tabs.SelectedItem = PlanTab;
        RunSummary.Visibility = Visibility.Collapsed;
        RunProgress.Maximum = Math.Max(toRun, 1);
        RunProgress.Value = 0;
        ProgressText.Text = $"0 / {toRun}";
        CancelButton.IsEnabled = true;
        CancelButton.Content = "Cancel";
        ProgressPanel.Visibility = Visibility.Visible;
        SetBusy(true, "Exporting... Inventor is busy until the run ends.");

        void Show(ExportResult r)
        {
            PlanRow? row = null;
            if (r.Status == ExportStatus.Unreadable) { /* shown in the Unreadable tab and the log */ }
            else if (r.Format is ExportFormat.Csv or ExportFormat.Xlsx) { if (nextTable < rows.Count - jobCount) row = rows[jobCount + nextTable++]; }
            else if (nextJob < jobCount)
            {
                bool wasPlanned = plan.Jobs[nextJob].State == JobState.Planned;
                row = rows[nextJob++];
                if (wasPlanned && r.Seconds != null)   // a job that actually ran (not cancelled)
                {
                    done++;
                    RunProgress.Value = done;
                    ProgressText.Text = $"{done} / {toRun}";
                }
            }
            if (row == null) return;
            row.State = r.Status.ToString();
            row.Detail = r.Detail ?? "";
            if (r.Seconds != null) row.Time = $"{r.Seconds.Value:F1} s";
        }

        try
        {
            var run = await Task.Run(() => ExportWorkflow.Run(plan, snapshot, config, outRoot,
                r => Dispatcher.BeginInvoke(() => Show(r)), token));
            ShowRunSummary(plan, run, outRoot);
            SetStatus(run.Cancelled ? "Cancelled. See the summary above the grid." : "Exports finished. See the summary above the grid.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The export run stopped with an error:" + Environment.NewLine + Environment.NewLine + ex.Message,
                "Run exports", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("Export run failed.");
        }
        finally
        {
            _runCancel.Dispose();
            _runCancel = null;
            ProgressPanel.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private void OnCancelRun(object sender, RoutedEventArgs e)
    {
        _runCancel?.Cancel();
        CancelButton.IsEnabled = false;
        CancelButton.Content = "Stopping after the current job...";
        SetStatus("Cancelling: the job in progress finishes first.");
    }

    private void ShowRunSummary(PlanResult plan, RunResult run, string outRoot)
    {
        var outcomes = ExportWorkflow.TaskOutcomes(plan.Tasks, run.Results);
        bool allComplete = !run.Cancelled && outcomes.All(o => o.Complete) && plan.Unreadable.Count == 0;
        var text = new StringBuilder();

        if (run.Cancelled)
        {
            int ran = run.Results.Count(r => r.Seconds != null);
            text.AppendLine($"CANCELLED after {ran} of {plan.PlannedCount} file(s). Jobs not run and all tables are logged as cancelled; no tables were written.");
        }
        foreach (var o in outcomes)
        {
            if (o.Complete) { text.AppendLine($"{o.TaskName}: complete ✓"); continue; }
            text.AppendLine($"{o.TaskName}: INCOMPLETE, {o.NotExported.Count} output(s) not exported " +
                            $"({string.Join(", ", o.NotExported.GroupBy(r => r.Status).Select(g => $"{g.Key} {g.Count()}"))}). " +
                            "Filter the grid by the status to see them.");
        }
        if (plan.Unreadable.Count > 0)
            text.AppendLine($"THE COMPONENT LIST IS INCOMPLETE: {plan.Unreadable.Count} component(s) could not be read (see the Unreadable tab).");

        text.AppendLine();
        foreach (var line in ExportResultLog.TimingSummary(run.Results))
            text.AppendLine("Timing: " + line);
        text.AppendLine($"All file jobs took {FormatDuration(run.FileJobsTime)}.");
        text.AppendLine(string.Join("   ·   ", ExportResultLog.Summary(run.Results)));
        text.AppendLine($"Log: {run.LogCsvPath}");
        if (run.ExclusionReportPath != null)
            text.AppendLine($"Left out by rules: {run.ExclusionReportPath}");

        RunSummaryText.Text = text.ToString().TrimEnd();
        RunSummaryText.Foreground = allComplete ? Brushes.DarkGreen : Brushes.DarkRed;
        RunSummary.Visibility = Visibility.Visible;
    }
}

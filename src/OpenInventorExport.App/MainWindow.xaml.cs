using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using OpenInventorExport.Core;
using OpenInventorExport.Core.Model;
using OpenInventorExport.Inventor;

namespace OpenInventorExport.App;

/// <summary>
/// The export window, step 3: choose config, output folder and task; collect from Inventor or load a
/// saved snapshot; show the summary. Plan and run come in the next steps. All work goes through
/// ExportWorkflow (the same code the CLI uses); this class only shows it.
/// </summary>
public partial class MainWindow : Window
{
    private const string AllTasks = "(all tasks)";

    private readonly AppSettings _settings = AppSettings.Load();
    private ExportConfig? _config;
    private string? _configPath;
    private Snapshot? _snapshot;

    public MainWindow(string? configFromArgs)
    {
        InitializeComponent();

        OutputBox.Text = _settings.OutputFolder ?? DefaultOutputFolder();
        string? config = configFromArgs ?? _settings.ConfigPath ?? FindDefaultConfig();
        if (config != null)
            LoadConfig(config);
        else
            ShowConfigProblem("No config file yet. Choose one with Browse.");
        FillTasks();
        TaskBox.SelectionChanged += (_, _) => ClearPlan();
        ClearPlan();
    }

    private string OutputFolder => OutputBox.Text.Trim();

    // ---------- Config ----------

    private void OnBrowseConfig(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a config file",
            Filter = "Config files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = _configPath != null ? Path.GetDirectoryName(_configPath) : null,
        };
        if (dlg.ShowDialog(this) == true)
            LoadConfig(dlg.FileName);
    }

    private void OnReloadConfig(object sender, RoutedEventArgs e)
    {
        string path = _configPath ?? ConfigBox.Text;
        if (!string.IsNullOrWhiteSpace(path))
            LoadConfig(path);
    }

    /// <summary>
    /// Loads and checks a config. A config that fails to load never replaces the one already loaded
    /// (as in the CLI), and the window says so plainly, so an old config cannot be used by mistake.
    /// </summary>
    private void LoadConfig(string path)
    {
        try
        {
            var config = ExportConfigFile.Load(path);   // resolved paths: never written back
            _config = config;
            _configPath = Path.GetFullPath(path);
            ConfigBox.Text = _configPath;
            ConfigStatus.Foreground = Brushes.DarkGreen;
            ConfigStatus.Text = $"Loaded: {config.Tasks.Count} task(s): {string.Join(", ", config.Tasks.Select(t => t.Name))}. " +
                                $"Unresolved components: {config.Unresolved}.";
            _settings.ConfigPath = _configPath;
            _settings.Save();
            SetStatus($"Config loaded: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            string still = _configPath != null ? $" Still using the previous config: {Path.GetFileName(_configPath)}." : "";
            ShowConfigProblem($"Config not loaded: {Path.GetFileName(path)}.{still}{Environment.NewLine}{ex.Message}");
            SetStatus("Config not loaded.");
        }
        FillTasks();
        ShowSummary();   // warnings depend on the config's Unresolved policy
        ClearPlan();
    }

    private void ShowConfigProblem(string text)
    {
        ConfigStatus.Foreground = Brushes.DarkRed;
        ConfigStatus.Text = text;
    }

    private void FillTasks()
    {
        string? previous = TaskBox.SelectedItem as string;
        TaskBox.Items.Clear();
        TaskBox.Items.Add(AllTasks);
        foreach (var t in _config?.Tasks ?? new List<ExportTask>())
            TaskBox.Items.Add(t.Name);
        TaskBox.SelectedItem = previous != null && TaskBox.Items.Contains(previous) ? previous : AllTasks;
    }

    /// <summary>The selected task name for ExportWorkflow, or null for all tasks.</summary>
    internal string? SelectedTask => TaskBox.SelectedItem is string s && s != AllTasks ? s : null;

    // ---------- Output folder ----------

    private void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose the output folder" };
        if (Directory.Exists(OutputFolder)) dlg.InitialDirectory = OutputFolder;
        if (dlg.ShowDialog(this) == true)
        {
            OutputBox.Text = dlg.FolderName;
            RememberOutput();
        }
    }

    private void OnOutputEdited(object sender, RoutedEventArgs e) => RememberOutput();

    private void RememberOutput()
    {
        if (_settings.OutputFolder == OutputFolder) return;
        _settings.OutputFolder = OutputFolder;
        _settings.Save();
        ClearPlan();   // the plan's paths point into the old folder
    }

    private void OnOpenOutput(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(OutputFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{OutputFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- Snapshot ----------

    private async void OnCollect(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            MessageBox.Show(this, "Choose an output folder first.", "Collect", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string outRoot = OutputFolder;
        SetBusy(true, "Collecting from Inventor... (about 27 components per second; Inventor is busy until it finishes)");
        try
        {
            // Off the window's thread so the window stays responsive; the CLI also calls Inventor from a non-UI thread.
            var result = await Task.Run(() => ExportWorkflow.Collect(outRoot));
            _snapshot = result.Snapshot;
            SnapshotStatus.Foreground = Brushes.Black;
            SnapshotStatus.Text = $"Collected {Path.GetFileName(_snapshot.SourceFile)}: {_snapshot.Components.Count} components in " +
                                  $"{FormatDuration(result.Elapsed)} ({result.ComponentsPerSecond:F1} per second). Saved: {result.SnapshotPath}";
            SetStatus("Collected.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Could not collect from Inventor." + Environment.NewLine + Environment.NewLine + ex.Message +
                Environment.NewLine + Environment.NewLine + "Is Inventor running, with the assembly as the active document?",
                "Collect", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("Collect failed.");
        }
        finally
        {
            SetBusy(false);
        }
        ShowSummary();
        ClearPlan();
    }

    private void OnLoadSnapshot(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Load a saved snapshot",
            Filter = "Snapshots (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(OutputFolder) ? OutputFolder : null,
        };
        if (dlg.ShowDialog(this) == true)
            LoadSnapshot(dlg.FileName);
    }

    /// <summary>Loads a saved snapshot (from the Load button, or the command line).</summary>
    internal void LoadSnapshot(string file)
    {
        try
        {
            _snapshot = ExportWorkflow.LoadSnapshot(file);
            SnapshotStatus.Foreground = Brushes.Black;
            SnapshotStatus.Text = $"Loaded saved snapshot {file} (collected {_snapshot.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}). " +
                                  "Collect again if the assembly has changed since.";
            SetStatus("Snapshot loaded.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load snapshot", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        ShowSummary();
        ClearPlan();
    }

    // ---------- Summary ----------

    private void ShowSummary()
    {
        SummaryGrid.Children.Clear();
        SummaryGrid.RowDefinitions.Clear();
        WarningText.Visibility = Visibility.Collapsed;
        DetailsBox.Text = "";
        var s = _snapshot;
        if (s == null) return;

        var parts = new[] { DocumentTypes.Part, DocumentTypes.SheetMetal };
        int unique = s.Components.Where(c => c.FullPath != null)
                                 .Select(c => PathUtil.StripMemberSuffix(c.FullPath!).ToLowerInvariant()).Distinct().Count();
        var byType = s.Components.GroupBy(c => c.DocumentType).OrderBy(g => g.Key)
                                 .Select(g => $"{g.Key} {g.Count()}");
        var skipped = s.Skipped.GroupBy(k => k.Reason).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}").ToList();
        var unreadable = SnapshotChecks.BlockingProblems(s);

        AddRow("Assembly", s.SourceFile ?? "(unknown)");
        AddRow("Inventor", s.InventorVersion ?? "(not recorded)");
        AddRow("Model state", s.ModelState ?? "(not recorded)");
        AddRow("Project", s.ProjectFile ?? "(not recorded)");
        AddRow("Collected", s.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        AddRow("Components", $"{s.Components.Count} occurrences, {unique} unique files");
        AddRow("By type", string.Join("   ·   ", byType));
        AddRow("Suppressed", s.Components.Count(c => c.IsSuppressed).ToString());
        AddRow("Content Center parts", $"{s.Components.Count(c => c.IsContentCenter == true)}" +
                                       $" (flag not recorded on {s.Components.Count(c => c.IsContentCenter == null && parts.Contains(c.DocumentType))} parts)");
        AddRow("Skipped records", skipped.Count == 0 ? "none" : string.Join("   ·   ", skipped));
        AddRow("Could not be read", unreadable.Count.ToString(), unreadable.Count > 0 ? Brushes.DarkRed : null);

        var warnings = new List<string>();
        if (unreadable.Count > 0)
        {
            warnings.Add($"THE COMPONENT LIST IS INCOMPLETE: {unreadable.Count} component(s) could not be read. " +
                         "An unresolved assembly's children cannot be seen at all, so more may be missing.");
            warnings.Add(_config?.Unresolved == UnresolvedPolicy.Warn
                ? "Exports will go ahead and log each one (Unresolved = Warn)."
                : "Exports will refuse until they are resolved (Unresolved = Block).");
            foreach (var k in unreadable.Take(10))
                warnings.Add($"   [{k.Reason}] {k.OccurrencePath} {k.Detail}");
            if (unreadable.Count > 10) warnings.Add($"   ...and {unreadable.Count - 10} more.");
        }
        warnings.AddRange(SnapshotSummary.Describe(s).Where(l => l.StartsWith("WARNING")));
        if (warnings.Count > 0)
        {
            WarningText.Text = string.Join(Environment.NewLine, warnings);
            WarningText.Visibility = Visibility.Visible;
        }

        DetailsBox.Text = string.Join(Environment.NewLine,
            SnapshotSummary.Describe(s).Concat(skipped.Select(x => "Skipped " + x)));
    }

    private void AddRow(string label, string value, Brush? valueColor = null)
    {
        int row = SummaryGrid.RowDefinitions.Count;
        SummaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var l = new TextBlock { Text = label, Foreground = Brushes.Gray, Margin = new Thickness(0, 1, 0, 1) };
        var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
        if (valueColor != null) { v.Foreground = valueColor; v.FontWeight = FontWeights.SemiBold; }
        Grid.SetRow(l, row);
        Grid.SetRow(v, row);
        Grid.SetColumn(v, 1);
        SummaryGrid.Children.Add(l);
        SummaryGrid.Children.Add(v);
    }

    // ---------- Helpers ----------

    private void SetBusy(bool busy, string? status = null)
    {
        CollectButton.IsEnabled = !busy;
        LoadButton.IsEnabled = !busy;
        ConfigRow.IsEnabled = !busy;
        OutputRow.IsEnabled = !busy;
        TaskRow.IsEnabled = !busy;
        UpdateButtons();
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
        if (status != null) SetStatus(status);
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private static string FormatDuration(TimeSpan t) =>
        t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{t.TotalSeconds:F1} s";

    private static string DefaultOutputFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Open Inventor Export");

    /// <summary>config/export.config.json in the exe's folder or any folder above it (same rule as the CLI). Null if absent.</summary>
    private static string? FindDefaultConfig()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "config", "export.config.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_runCancel != null)
        {
            MessageBox.Show(this, "Exports are running. Press Cancel and wait until the run stops before closing.",
                "Open Inventor Export", MessageBoxButton.OK, MessageBoxImage.Information);
            e.Cancel = true;
            return;
        }
        RememberOutput();
    }
}

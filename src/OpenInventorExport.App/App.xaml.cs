using System.Windows;

namespace OpenInventorExport.App;

/// <summary>
/// Starts the export window. Optional arguments: a config file to load, then a saved snapshot to load
/// (handy for testing without Inventor).
/// </summary>
public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow(e.Args.Length > 0 ? e.Args[0] : null);
        window.Show();
        if (e.Args.Length > 1)
            window.LoadSnapshot(e.Args[1]);
    }
}

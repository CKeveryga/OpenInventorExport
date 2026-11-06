using System.Windows;

namespace OpenInventorExport.App;

/// <summary>Starts the export window. An optional first argument is a config file to load.</summary>
public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow(e.Args.Length > 0 ? e.Args[0] : null);
        window.Show();
    }
}

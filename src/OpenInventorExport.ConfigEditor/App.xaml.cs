using System.Windows;

namespace OpenInventorExport.ConfigEditor;

/// <summary>Starts the editor. An optional first argument is a config file to open.</summary>
public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow();
        window.Show();
        if (e.Args.Length > 0)
            window.OpenFile(e.Args[0]);
    }
}

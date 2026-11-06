using System.Runtime.InteropServices;
using System.Windows.Forms;
using Inv = global::Inventor;

namespace OpenInventorExport.AddIn;

/// <summary>
/// The class Inventor creates when it loads the add-in. Step 1 ("hello"): adds one button,
/// "Open Inventor Export", to a panel on the Tools tab of the assembly ribbon. Clicking it shows
/// a message with the Inventor version and the active document. Later the button starts the UI window.
/// Read-only: nothing here touches a document.
/// </summary>
[Guid(ClassId)]
[ComVisible(true)]
public class AddInServer : Inv.ApplicationAddInServer
{
    /// <summary>Must match the manifest's clsid and the .addin file's ClassId/ClientId.</summary>
    public const string ClassId = "0CC8A77E-188C-46AD-8D2A-E9DE6BC374D6";
    private const string ClientId = "{" + ClassId + "}";

    private const string ButtonName = "OpenInventorExport:OpenButton";
    private const string PanelName = "OpenInventorExport:Panel";

    private Inv.Application? _app;
    private Inv.ButtonDefinition? _button;
    private Inv.UserInterfaceEvents? _uiEvents;

    public void Activate(Inv.ApplicationAddInSite addInSiteObject, bool firstTime)
    {
        try
        {
            _app = addInSiteObject.Application;

            _button = _app.CommandManager.ControlDefinitions.AddButtonDefinition(
                "Open Inventor Export", ButtonName, Inv.CommandTypesEnum.kQueryOnlyCmdType, ClientId,
                "Export the active assembly with Open Inventor Export", "Open Inventor Export");
            _button.OnExecute += OnButtonExecute;

            AddToRibbon();

            // Inventor rebuilds the ribbon when the user resets it; add the panel again then.
            _uiEvents = _app.UserInterfaceManager.UserInterfaceEvents;
            _uiEvents.OnResetRibbonInterface += OnResetRibbon;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Open Inventor Export could not start:" + System.Environment.NewLine + ex,
                "Open Inventor Export");
        }
    }

    /// <summary>Panel on the assembly ribbon's Tools tab. Reuses the panel if Inventor kept it from an earlier session.</summary>
    private void AddToRibbon()
    {
        if (_app == null || _button == null) return;

        Inv.RibbonTab tools = _app.UserInterfaceManager.Ribbons["Assembly"].RibbonTabs["id_TabTools"];

        Inv.RibbonPanel? panel = null;
        foreach (Inv.RibbonPanel p in tools.RibbonPanels)
            if (p.InternalName == PanelName) { panel = p; break; }
        panel ??= tools.RibbonPanels.Add("Open Inventor Export", PanelName, ClientId);

        foreach (Inv.CommandControl c in panel.CommandControls)
            if (c.InternalName == ButtonName) return;   // already there
        panel.CommandControls.AddButton(_button, true);
    }

    private void OnResetRibbon(Inv.NameValueMap context)
    {
        try { AddToRibbon(); }
        catch (Exception ex) { MessageBox.Show(ex.ToString(), "Open Inventor Export"); }
    }

    private void OnButtonExecute(Inv.NameValueMap context)
    {
        try
        {
            string version = _app?.SoftwareVersion.DisplayName ?? "(unknown)";
            string doc = _app?.ActiveDocument?.DisplayName ?? "(no active document)";
            MessageBox.Show(
                $"Open Inventor Export add-in is loaded.{System.Environment.NewLine}" +
                $"Inventor: {version}{System.Environment.NewLine}" +
                $"Add-in build: {BuildName}{System.Environment.NewLine}" +
                $"Active document: {doc}",
                "Open Inventor Export");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Open Inventor Export");
        }
    }

#if NET48
    private const string Target = "net48";
#else
    private const string Target = "net8.0-windows";
#endif
    /// <summary>Which of the two builds Inventor loaded, and the .NET runtime it runs on.</summary>
    private static string BuildName => $"{Target}, runtime {RuntimeInformation.FrameworkDescription}";

    public void Deactivate()
    {
        try
        {
            if (_uiEvents != null) _uiEvents.OnResetRibbonInterface -= OnResetRibbon;
            if (_button != null) _button.OnExecute -= OnButtonExecute;
        }
        catch { /* Inventor is shutting down; nothing useful to report */ }

        _uiEvents = null;
        _button = null;
        if (_app != null) Marshal.ReleaseComObject(_app);
        _app = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    /// <summary>Obsolete in Inventor's API (buttons have their own events), but required by the interface.</summary>
    public void ExecuteCommand(int commandID) { }

    /// <summary>No automation interface is offered to other programs.</summary>
    public object? Automation => null;
}

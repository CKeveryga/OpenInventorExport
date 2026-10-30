using Inv = global::Inventor;

namespace OpenInventorExport.Inventor;

internal static class Translators
{
    /// <summary>Looks up a translator add-in by ID and activates it if needed.</summary>
    public static Inv.TranslatorAddIn Get(Inv.Application app, string id)
    {
        var t = (Inv.TranslatorAddIn)app.ApplicationAddIns.ItemById[id];
        if (!t.Activated) t.Activate();
        return t;
    }
}
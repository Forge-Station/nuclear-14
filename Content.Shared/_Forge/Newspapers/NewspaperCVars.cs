using Robust.Shared.Configuration;

namespace Content.Shared._Forge.Newspapers;

[CVarDefs]
public sealed class NewspaperCVars
{
    /// <summary>Shows the layout editing mode. Choosing and filling existing templates remains available.</summary>
    public static readonly CVarDef<bool> LayoutEditorEnabled =
        CVarDef.Create("newspapers.layout_editor_enabled", false, CVar.SERVER | CVar.REPLICATED);
}

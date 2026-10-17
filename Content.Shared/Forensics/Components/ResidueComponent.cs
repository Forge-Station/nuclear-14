using Robust.Shared.GameStates;
using Content.Shared.Forensics.Components;

namespace Content.Shared.Forensics.Components;

/// <summary>
/// This controls residues left on items
/// which the forensics system uses.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ResidueComponent : Component
{
    /// <summary>
    /// Specifies residue, for now only color.
    /// </summary>
    [DataField]
    public LocId ResidueAdjective = "residue-unknown";

    /// <summary>
    /// Leaves color of cleaning product for ResidueAdjective to use.
    /// </summary>
    [DataField]
    public string? ResidueColor;
}

using Content.Shared._Forge.Warfront;

namespace Content.Server._Forge.Warfront.FactionPoints;

[RegisterComponent]
public sealed partial class FactionPointsComponent : Component
{
    [DataField]
    public WarfrontFaction Faction;

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public int Balance;
}

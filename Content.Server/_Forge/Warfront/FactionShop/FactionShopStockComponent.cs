using Content.Shared._Forge.Warfront;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Warfront.FactionShop;

[RegisterComponent]
public sealed partial class FactionShopStockComponent : Component
{
    [DataField]
    public WarfrontFaction Faction;

    [DataField]
    public Dictionary<EntProtoId, int> AvailableListings = new();

    [DataField]
    public TimeSpan NextRotationTime;
}

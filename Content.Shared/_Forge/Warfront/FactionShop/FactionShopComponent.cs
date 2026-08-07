using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Warfront.FactionShop;

[RegisterComponent]
public sealed partial class FactionShopComponent : Component
{
    [DataField]
    public WarfrontFaction Faction;

    [DataField]
    public Dictionary<EntProtoId, int> FullCatalog = new();

    [DataField]
    public TimeSpan RotationInterval = TimeSpan.FromMinutes(1);

    [DataField]
    public int OffersPerRotation = 3;
}

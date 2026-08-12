using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Warfront.FactionShop;

[Serializable, NetSerializable]
public sealed class FactionShopBoundUserInterfaceState : BoundUserInterfaceState
{
    public int Balance;
    public WarfrontFaction Faction;

    public Dictionary<EntProtoId, int> AvailableListings = new();
    public TimeSpan NextRotationTime;
}

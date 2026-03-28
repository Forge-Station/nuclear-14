using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Serializable, NetSerializable]
public enum ContractExecutionKind : byte
{
    InventoryDelivery = 0,
    TrackedDeliveryObjective,
    HuntObjective,
    RepairObjective,
    GhostRoleObjective
}

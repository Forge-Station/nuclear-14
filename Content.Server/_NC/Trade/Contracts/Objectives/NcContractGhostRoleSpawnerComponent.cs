using Robust.Shared.GameObjects;

namespace Content.Server._NC.Trade;

[RegisterComponent]
public sealed partial class NcContractGhostRoleSpawnerComponent : Component
{
    [DataField("prototype", required: true)]
    public string TargetPrototype = string.Empty;

    public bool Claimed;
}

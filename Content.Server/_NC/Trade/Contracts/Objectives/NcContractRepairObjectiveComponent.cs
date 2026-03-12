using Robust.Shared.GameObjects;

namespace Content.Server._NC.Trade;

[RegisterComponent]
public sealed partial class NcContractRepairObjectiveComponent : Component
{
    [DataField]
    public string ToolQuality = "Welding";

    [DataField]
    public float DoAfterSeconds = 2f;
}

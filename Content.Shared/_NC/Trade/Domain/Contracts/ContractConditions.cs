using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

/// <summary>
/// Lightweight, YAML-facing condition descriptor for contract extensions.
/// Core code only stores and dispatches these descriptors; concrete behavior lives in
/// server-side condition handlers registered by the base system or downstream projects.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class ContractConditionDef
{
    [DataField("type", required: true)]
    public string Type { get; set; } = string.Empty;

    [DataField("id")]
    public string Id { get; set; } = string.Empty;

    [DataField("phase")]
    public ContractConditionPhase Phase { get; set; } = ContractConditionPhase.TakeAndClaim;

    [DataField("invert")]
    public bool Invert { get; set; }

    [DataField("args")]
    public Dictionary<string, string> Args { get; set; } = new();
}

[Serializable, NetSerializable]
public enum ContractConditionPhase : byte
{
    Take = 0,
    Claim = 1,
    TakeAndClaim = 2,
    Offer = 3,
    Progress = 4,
    Always = 5
}

using Robust.Shared.Prototypes;

namespace Content.Server.NPC.HTN;

/// <summary>
/// Represents a network of multiple tasks. This gets expanded out to its relevant nodes.
/// </summary>
[Prototype("htnCompound")]
public sealed partial class HTNCompoundPrototype : IPrototype
{
    [IdDataField] public string ID { get; set; } = string.Empty;

    [DataField("branches", required: true)]
    public List<HTNBranch> Branches = new();
}

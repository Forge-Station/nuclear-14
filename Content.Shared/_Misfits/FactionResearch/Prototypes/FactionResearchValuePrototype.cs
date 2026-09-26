using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.FactionResearch.Prototypes;

[Prototype("factionResearchValue")]
public sealed partial class FactionResearchValuePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public float Multiplier = 1f;

    [DataField]
    public int FlatBonus;
}

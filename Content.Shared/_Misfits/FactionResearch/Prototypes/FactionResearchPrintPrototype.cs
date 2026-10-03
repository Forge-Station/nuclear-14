using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.FactionResearch.Prototypes;

[Prototype("factionResearchPrint")]
public sealed partial class FactionResearchPrintPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public LocId? Name;

    [DataField]
    public LocId? Category;

    [DataField(required: true)]
    public ProtoId<DepartmentPrototype> Faction;

    [DataField]
    public int Tier = 1;

    [DataField]
    public int Cost = 500;

    [DataField]
    public EntProtoId? Item;

    [DataField]
    public ProtoId<RandomResearchSheetPrototype>? RandomSheet;
}

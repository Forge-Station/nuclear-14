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

    /// <summary>
    /// Tier-chain group key. Prints sharing the same faction + group form one progression:
    /// tier N requires tier N-1 of the same group to be learned. If empty, the group is derived
    /// from <see cref="Item"/>'s id (Weapons/Armor) for backwards compatibility.
    /// </summary>
    [DataField]
    public string Group = string.Empty;

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

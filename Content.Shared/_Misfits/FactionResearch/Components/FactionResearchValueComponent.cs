using Content.Shared._Misfits.FactionResearch.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.FactionResearch.Components;

[RegisterComponent]
public sealed partial class FactionResearchValueComponent : Component
{
    [DataField]
    public ProtoId<FactionResearchValuePrototype>? Category;

    [DataField]
    public int? PointsOverride;
}

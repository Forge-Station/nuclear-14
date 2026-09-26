using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.FactionResearch.Components;

[RegisterComponent]
public sealed partial class FactionResearchComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<DepartmentPrototype>, int> Points = new();
}

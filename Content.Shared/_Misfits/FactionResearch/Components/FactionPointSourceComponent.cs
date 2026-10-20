using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.FactionResearch.Components;

[RegisterComponent]
public sealed partial class FactionPointSourceComponent : Component
{
    [DataField(required: true)]
    public ProtoId<DepartmentPrototype> Faction;

    [DataField]
    public int PointsPerSecond = 5;

    [DataField]
    public float Radius = 3f;

    [DataField]
    public bool Active = true;

    public TimeSpan NextUpdateTime;
}

using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.FactionResearch.Components;

[RegisterComponent]
public sealed partial class FactionResearchComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<DepartmentPrototype>, int> Points = new();

    /// <summary>
    /// Print ids that this bench's faction has already researched. Learned (non-random) prints can be
    /// printed again for free and unlock the next tier of the same item group.
    /// </summary>
    [DataField]
    public HashSet<string> LearnedPrints = new();
}

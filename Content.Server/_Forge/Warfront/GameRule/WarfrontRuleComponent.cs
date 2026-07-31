using Content.Shared._Forge.Warfront;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Warfront.GameRule;

[RegisterComponent]
public sealed partial class WarfrontRuleComponent : Component
{
    [DataField]
    public WarfrontFaction? Winner;

    [DataField]
    public TimeSpan RestartDelay = TimeSpan.FromSeconds(20);

    [DataField]
    public List<ProtoId<JobPrototype>> NcrJobs = new();

    [DataField]
    public List<ProtoId<JobPrototype>> LegionJobs = new();
}

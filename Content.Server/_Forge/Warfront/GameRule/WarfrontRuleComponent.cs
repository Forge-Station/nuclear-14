using Content.Shared._Forge.Warfront;

namespace Content.Server._Forge.Warfront.GameRule;

[RegisterComponent]
public sealed partial class WarfrontRuleComponent : Component
{
    [DataField]
    public WarfrontFaction? Winner;

    [DataField]
    public TimeSpan RestartDelay = TimeSpan.FromSeconds(20);
}

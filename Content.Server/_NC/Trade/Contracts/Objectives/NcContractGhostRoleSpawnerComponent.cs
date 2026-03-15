using Content.Shared.Customization.Systems;
using Robust.Shared.GameObjects;

namespace Content.Server._NC.Trade;

[RegisterComponent]
public sealed partial class NcContractGhostRoleSpawnerComponent : Component
{
    [DataField("prototype", required: true)]
    public string TargetPrototype = string.Empty;

    public List<CharacterRequirement> Requirements = new();

    public bool Claimed;
}

public sealed class GhostRoleGetRequirementsEvent : EntityEventArgs
{
    public List<CharacterRequirement>? Requirements { get; set; }

    public GhostRoleGetRequirementsEvent(List<CharacterRequirement>? requirements)
    {
        Requirements = requirements;
    }
}

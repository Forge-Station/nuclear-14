using Content.Shared.Customization.Systems;


namespace Content.Server._NC.Trade;


[RegisterComponent]
public sealed partial class NcContractGhostRoleSpawnerComponent : Component
{
    public bool Claimed;

    public List<CharacterRequirement> Requirements = new();

    [DataField("prototype", required: true)]
    public string TargetPrototype = string.Empty;
}

public sealed class GhostRoleGetRequirementsEvent : EntityEventArgs
{
    public GhostRoleGetRequirementsEvent(List<CharacterRequirement>? requirements)
    {
        Requirements = requirements;
    }

    public List<CharacterRequirement>? Requirements { get; set; }
}

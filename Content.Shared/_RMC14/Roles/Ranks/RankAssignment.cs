// Forge-Change
using Content.Shared.Customization.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Roles.Ranks;

/// <summary>
/// Associates a rank with the character requirements needed to receive it.
/// Rank assignments on a job are evaluated in definition order.
/// </summary>
[DataDefinition]
public sealed partial class RankAssignment
{
    [DataField(required: true)]
    public ProtoId<RankPrototype> Rank { get; private set; }

    [DataField]
    public List<CharacterRequirement> Requirements { get; private set; } = new();
}

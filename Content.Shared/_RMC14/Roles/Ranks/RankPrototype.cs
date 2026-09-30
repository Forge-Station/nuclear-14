// Forge-Change
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Roles.Ranks;

/// <summary>
/// Describes a cosmetic rank that can be assigned to a character.
/// </summary>
[Prototype]
public sealed partial class RankPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Full localized rank name shown when examining the character.
    /// </summary>
    [DataField(required: true)]
    public LocId Name { get; private set; } = default!;
}

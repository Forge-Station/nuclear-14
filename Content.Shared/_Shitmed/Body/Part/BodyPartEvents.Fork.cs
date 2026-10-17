using Content.Shared._Shitmed.Targeting;
using Content.Shared.Damage;

namespace Content.Shared.Body.Part;

/// <summary>
/// Raised on a body when a part is attached to it.
/// Fork-owned: upstream's Nubody rewrite deleted Content.Shared/Body/Part and raises
/// its own OrganInsertedIntoEvent instead.
/// </summary>
[ByRefEvent]
public readonly record struct BodyPartAddedEvent(string Slot, Entity<BodyPartComponent> Part);

/// <summary>
/// Raised on a body when a part is removed from it.
/// </summary>
[ByRefEvent]
public readonly record struct BodyPartRemovedEvent(string Slot, Entity<BodyPartComponent> Part);

/// <summary>
/// Raised on a body before damage is dealt, so a part can evade or cancel it.
/// Fork-owned: used to live on upstream's DamageableSystem, which was rewritten.
/// </summary>
[ByRefEvent]
public record struct TryChangePartDamageEvent(
    DamageSpecifier Damage,
    EntityUid? Origin = null,
    TargetBodyPart? TargetPart = null,
    bool IgnoreResistances = false,
    bool CanSever = true,
    bool CanEvade = false,
    float PartMultiplier = 1.00f,
    bool Evaded = false,
    bool Cancelled = false);

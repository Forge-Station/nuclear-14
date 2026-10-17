using Content.Shared.Body.Organ;
namespace Content.Shared._Shitmed.Body.Organ;

public readonly record struct OrganComponentsModifyEvent(EntityUid Body, bool Add);

[ByRefEvent]
public readonly record struct OrganEnableChangedEvent(bool Enabled);

[ByRefEvent]
public readonly record struct OrganEnabledEvent(Entity<ShitmedOrganComponent> Organ);

[ByRefEvent]
public readonly record struct OrganDisabledEvent(Entity<ShitmedOrganComponent> Organ);

public readonly record struct OrganDamageChangedEvent(bool DamageIncreased);

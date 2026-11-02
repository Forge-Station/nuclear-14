using Robust.Shared.Map;

namespace Content.Server._Forge.Structures.BallisticCover;

/// <summary>
/// Server-side shot snapshot. Moving the shooter does not change an existing bullet.
/// </summary>
[RegisterComponent]
public sealed partial class CoverProjectileComponent : Component
{
    [DataField]
    public EntityCoordinates Origin;

    [DataField]
    public bool CanPass;

    // Only covers actually penetrated: repeated contact must not deal damage twice.
    public HashSet<EntityUid>? PenetratedCovers;
    public bool PenetrationPending;
    public EntityUid? PenetrationContact;
    public bool OriginalDeleteOnCollide;
    public float PendingDamageMultiplier;
}

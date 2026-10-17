using Content.Shared.Body.Part;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.Body.Systems;

/// <summary>
/// Host class for Shitmed's body-part partials (targeting, part appearance, integrity).
///
/// Fork-owned, and NOT upstream's body system - upstream's is
/// <see cref="Content.Shared.Body.BodySystem"/>, which owns the organ container and the
/// parent/child graph. This one carries only what Shitmed adds on top: part integrity and
/// severing, the targeting doll, part sprites.
///
/// The name is kept as SharedBodySystem because upstream freed it (its system is now
/// BodySystem) and the fork's partials and consumers already use it, so renaming would
/// churn every call site for no behavioural gain.
/// </summary>
public partial class SharedBodySystem : EntitySystem
{
    [Dependency] protected readonly BodySystem Body = default!;
    [Dependency] protected readonly OrganRelationSystem Relations = default!;
    [Dependency] protected readonly DetachableOrganSystem Detachable = default!;
    [Dependency] protected readonly IGameTiming _timing = default!;
    [Dependency] protected readonly IPrototypeManager Prototypes = default!;
    [Dependency] protected readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializeIntegrityQueue();
    }

    /// <summary>
    /// Keeps <see cref="BodyPartComponent.Body"/> in step with upstream's organ container.
    /// </summary>
    protected void SyncPartBody(Entity<BodyPartComponent> part, EntityUid? body)
    {
        if (part.Comp.Body == body)
            return;

        part.Comp.Body = body;
        Dirty(part);
    }
}

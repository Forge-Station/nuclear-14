using Content.Server.Projectiles;
using Content.Shared._Forge.Structures.BallisticCover;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Events;

namespace Content.Server._Forge.Structures.BallisticCover;

/// <summary>
/// Captures the specific fixture before the engine handles the hit. An additional solid
/// fixture on a compound entity must not inherit low-cover penetration behavior.
/// </summary>
public sealed class BallisticCoverPenetrationCaptureSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MetaDataComponent, StartCollideEvent>(OnContact,
            before: new[] { typeof(ProjectileSystem), typeof(BallisticCoverSystem) });
    }

    private void OnContact(Entity<MetaDataComponent> ent, ref StartCollideEvent args)
    {
        if (!TryComp<CoverProjectileComponent>(ent, out var shot))
            return;
        shot.PenetrationContact = null;
        if (args.OurFixtureId == SharedProjectileSystem.ProjectileFixture && args.OtherFixture.Hard &&
            TryComp<BallisticCoverComponent>(args.OtherEntity, out var cover) &&
            args.OtherFixtureId == cover.Fixture)
            shot.PenetrationContact = args.OtherEntity;
    }
}

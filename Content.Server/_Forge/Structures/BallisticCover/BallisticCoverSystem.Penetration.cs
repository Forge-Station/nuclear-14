using Content.Server.Explosion.Components;
using Content.Server.Projectiles;
using Content.Shared._Forge.Structures.BallisticCover;
using Content.Shared.Projectiles;
using System.Numerics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;

namespace Content.Server._Forge.Structures.BallisticCover;

public sealed partial class BallisticCoverSystem
{
    [Dependency] private readonly IRobustRandom _penetrationRandom = default!;
    // After penetration, end movement at the contact point. Otherwise, taking a full solver
    // step from the advanced position could skip the next barricade.
    private readonly Dictionary<EntityUid, Vector2> _penetrationVelocities = new();

    private void InitializePenetration()
    {
        SubscribeLocalEvent<PhysicsUpdateAfterSolveEvent>(OnAfterPenetrationStep);
        SubscribeLocalEvent<CoverProjectileComponent, ProjectileHitEvent>(OnPenetrationHit);
        SubscribeLocalEvent<CoverProjectileComponent, StartCollideEvent>(OnAfterPenetration,
            after: new[] { typeof(ProjectileSystem) });
    }

    private void OnPenetrationHit(Entity<CoverProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        // The engine raises this event after checking reflection.
        // Explosive, contact-triggered, and embeddable projectiles cannot penetrate cover.
        if (ent.Comp.PenetrationContact != args.Target || !ent.Comp.CanPass ||
            HasComp<ExplosiveComponent>(ent) || HasComp<TriggerOnCollideComponent>(ent) ||
            HasComp<EmbeddableProjectileComponent>(ent) ||
            !TryComp<ProjectileComponent>(ent, out var projectile) ||
            !args.Damage.DamageDict.TryGetValue("Piercing", out var piercing) || piercing <= 0 ||
            !TryComp<BallisticCoverComponent>(args.Target, out var cover) ||
            cover.PenetrationChance <= 0 ||
            !_penetrationRandom.Prob(Math.Clamp(cover.PenetrationChance, 0f, 1f)))
            return;

        // Damage, sounds, and effects remain in ProjectileSystem. Keep full damage until
        // its handler finishes, temporarily preventing the bullet from being deleted.
        ent.Comp.PenetrationPending = true;
        ent.Comp.OriginalDeleteOnCollide = projectile.DeleteOnCollide;
        ent.Comp.PendingDamageMultiplier = Math.Clamp(cover.PenetrationDamageMultiplier, 0f, 1f);
        (ent.Comp.PenetratedCovers ??= new()).Add(args.Target);
        projectile.DeleteOnCollide = false;
    }

    private void OnAfterPenetration(Entity<CoverProjectileComponent> ent, ref StartCollideEvent args)
    {
        ent.Comp.PenetrationContact = null;
        if (!ent.Comp.PenetrationPending || !TryComp<ProjectileComponent>(ent, out var projectile))
            return;

        ent.Comp.PenetrationPending = false;
        projectile.DeleteOnCollide = ent.Comp.OriginalDeleteOnCollide;
        projectile.Damage *= ent.Comp.PendingDamageMultiplier;
        projectile.DamagedEntity = false;
        // A bullet with no remaining damage must not deliver effects to the next target.
        if (!projectile.Damage.AnyPositive())
        {
            QueueDel(ent);
            return;
        }

        if (TryComp<PhysicsComponent>(ent, out var body))
        {
            _penetrationVelocities.TryAdd(ent.Owner, body.LinearVelocity);
            _physics.SetLinearVelocity(ent.Owner, Vector2.Zero, body: body);
        }
    }

    private void OnAfterPenetrationStep(ref PhysicsUpdateAfterSolveEvent args)
    {
        foreach (var (uid, velocity) in _penetrationVelocities)
        {
            if (!TerminatingOrDeleted(uid) && TryComp<PhysicsComponent>(uid, out var body))
                _physics.SetLinearVelocity(uid, velocity, body: body);
        }
        _penetrationVelocities.Clear();
    }
}

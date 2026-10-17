using Content.Shared.Body.Events;
using Content.Server.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared._Shitmed.Body.Organ;
using Content.Server._Shitmed.DelayedDeath;
using Content.Shared.Body.Components;
using Content.Shared.Body;

namespace Content.Server._Shitmed.Body.Organ;

public sealed class HeartSystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _bodySystem = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HeartComponent, OrganGotInsertedEvent>(HandleAddition);
        SubscribeLocalEvent<HeartComponent, OrganGotRemovedEvent>(HandleRemoval);
    }

    private void HandleRemoval(EntityUid uid, HeartComponent _, ref OrganGotRemovedEvent args)
    {
        if (TerminatingOrDeleted(uid) || TerminatingOrDeleted(args.Target))
            return;

        // TODO: Add some form of very violent bleeding effect.
        EnsureComp<DelayedDeathComponent>(args.Target);
    }

    private void HandleAddition(EntityUid uid, HeartComponent _, ref OrganGotInsertedEvent args)
    {
        if (TerminatingOrDeleted(uid) || TerminatingOrDeleted(args.Target))
            return;

        if (_bodySystem.TryGetBodyOrganComponents<BrainComponent>(args.Target, out var _))
            RemComp<DelayedDeathComponent>(args.Target);
    }
    // Shitmed-End
}

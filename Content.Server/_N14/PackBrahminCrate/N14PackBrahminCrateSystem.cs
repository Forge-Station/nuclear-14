using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Popups;
using Content.Shared._N14.PackBrahminCrate;
using Content.Shared.Interaction;
using Robust.Shared.Map;

namespace Content.Server._N14.PackBrahminCrate;

public sealed class N14PackBrahminCrateSystem : EntitySystem
{
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        // InteractHandEvent = left click with empty hand.
        // EntityStorage opens via ActivateInWorldEvent (E key) — no conflict.
        SubscribeLocalEvent<N14PackBrahminCrateComponent, InteractHandEvent>(OnInteractHand);
    }

    private void OnInteractHand(EntityUid uid, N14PackBrahminCrateComponent comp, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (!comp.IsFollowing)
        {
            StartFollowing(uid, comp, args.User);
        }
        else if (comp.FollowTarget == args.User)
        {
            StopFollowing(uid, comp, args.User);
        }
        else
        {
            // Different player — transfer ownership
            StartFollowing(uid, comp, args.User);
        }
    }

    private void StartFollowing(EntityUid uid, N14PackBrahminCrateComponent comp, EntityUid target)
    {
        comp.IsFollowing = true;
        comp.FollowTarget = target;

        _npc.SetBlackboard(uid, NPCBlackboard.FollowTarget,
            new EntityCoordinates(target, Vector2.Zero));

        // Popup at the PLAYER's position so it is always visible on screen
        _popup.PopupEntity(Loc.GetString("n14-pack-crate-follow-start"), target, target);
    }

    private void StopFollowing(EntityUid uid, N14PackBrahminCrateComponent comp, EntityUid? notifyUser = null)
    {
        comp.IsFollowing = false;
        comp.FollowTarget = null;

        if (TryComp<HTNComponent>(uid, out var htn))
            htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);

        if (notifyUser.HasValue)
            _popup.PopupEntity(Loc.GetString("n14-pack-crate-follow-stop"), notifyUser.Value, notifyUser.Value);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<N14PackBrahminCrateComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.IsFollowing || comp.FollowTarget == null)
                continue;

            if (TerminatingOrDeleted(comp.FollowTarget.Value))
                StopFollowing(uid, comp);
        }
    }
}

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

        SubscribeLocalEvent<N14PackBrahminCrateComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnActivate(EntityUid uid, N14PackBrahminCrateComponent comp, ActivateInWorldEvent args)
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
            // Different player clicked — transfer ownership
            StartFollowing(uid, comp, args.User);
        }
    }

    private void StartFollowing(EntityUid uid, N14PackBrahminCrateComponent comp, EntityUid target)
    {
        comp.IsFollowing = true;
        comp.FollowTarget = target;

        // Set HTN blackboard FollowTarget so the N14PackCrateCompound picks it up
        _npc.SetBlackboard(uid, NPCBlackboard.FollowTarget,
            new EntityCoordinates(target, Vector2.Zero));

        _popup.PopupEntity(Loc.GetString("n14-pack-crate-follow-start"), uid, target);
    }

    private void StopFollowing(EntityUid uid, N14PackBrahminCrateComponent comp, EntityUid? notifyUser = null)
    {
        comp.IsFollowing = false;
        comp.FollowTarget = null;

        // Clear the HTN follow target so the mob falls back to idle
        if (TryComp<HTNComponent>(uid, out var htn))
            htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);

        if (notifyUser.HasValue)
            _popup.PopupEntity(Loc.GetString("n14-pack-crate-follow-stop"), uid, notifyUser.Value);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<N14PackBrahminCrateComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.IsFollowing || comp.FollowTarget == null)
                continue;

            // Clean up if the target entity was deleted (player disconnected/died)
            if (TerminatingOrDeleted(comp.FollowTarget.Value))
                StopFollowing(uid, comp);
        }
    }
}
